/* WARNING! This program and source code is owned and licensed by 
   Modulus Financial Engineering, Inc. http://www.modulusfe.com
   Viewing or use this code requires your acceptance of the license
   agreement found at http://www.modulusfe.com/support/license.pdf
   Removal of this comment is a violation of the license agreement.
   Copyright 2002-2016 by Modulus Financial Engineering, Inc. */

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace NDAXCore
{       
    public class DataFeed
    {
        //internal class for total or partial order execution 
        private class SingleExecution
        {
            //new received order ID
            public string NewOrderID;
            //previously received order ID
            public string ExistingOrderID;
            //filled price
            public decimal Price;
            //filled quantity
            public decimal Quantity;
        }
        
        public event Action<Tick> OnTick;
        public event Action<string, Execution> OnExecution;
        public event Action<string, Account> OnAccountInfo;
        public event Action<string, string, string> OnMessageResponse;

        private List<User> _users = new List<User>();
        //current market and limit orders  
        private List<NewOrderRequest> _activeOrders = new List<NewOrderRequest>();
        //current stop orders 
        private List<NewOrderRequest> _stopOorders = new List<NewOrderRequest>();
        //activated by current execution stop orders
        private Queue<NewOrderRequest> _activatedStopOrders = new Queue<NewOrderRequest>();
        //current orders executions
        private List<Execution> _executions = new List<Execution>();
        private object _tradingLocker = new object();
        //users requests queue
        private Queue<object> _requests = new Queue<object>();
        //indicates is system ready to handle user requests
        private bool _started = false;
        //system event to process market and limit orders
        private ManualResetEvent _processOrdersEvent = new ManualResetEvent(false);
        //system event to process activated stop orders
        private ManualResetEvent _processStopOrdersEvent = new ManualResetEvent(false);
        //thread to process market and limit orders
        private Thread _processOrdersThread = null;
        //thread to process activated stop orders
        private Thread _processStopOrdersThread = null;
        //thread to process end of current day trading session
        private Thread _processExchangeSessionThread = null;
        private string _connectionString = ConfigurationManager.ConnectionStrings["NDAXCore"].ConnectionString;
        //available exchanges
        private List<ExchangeSettingsEx> _exchanges = new List<ExchangeSettingsEx>();
        //available currencies
        private List<Currency> _currencies = new List<Currency>();
        //historical data cache
        private Cache _cache = null;
        //initial date used for order duration processing
        private DateTime _UNIX_START = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        //start exchange engine
        public string Start()
        {
            string result = "";
            SqlConnection connection = new SqlConnection(_connectionString);
            SqlTransaction transaction = null;
            SqlDataReader reader = null;
            try
            {
                connection.Open();
                transaction = connection.BeginTransaction();
                SqlCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT u.[UserName],u.[Password], a.[Name], a.[Balance], a.[Currency] FROM [Users] u, [Accounts] a WHERE u.[UserName] = a.[UserName] ORDER BY u.[UserName], a.[Name]";
                reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    //retreive users account data from DB
                    lock (_tradingLocker)
                    {
                        while (reader.Read())
                        {
                            string userName = reader.GetString(0);
                            if (_users.FirstOrDefault(item => item.UserName == userName) == null)
                            {
                                _users.Add(new User() { UserName = userName, Password = reader.GetString(1), Accounts = new List<Account>() });
                            }
                            _users.First(item => item.UserName == userName).Accounts.Add(new Account() { Name = reader.GetString(2), Balance = reader.GetDecimal(3), Currency = reader.GetString(4) });
                        }
                    }
                }
                reader.Close();
                command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT e.[Name], e.[StartTime], e.[EndTime], e.[CommonCurrency], s.[ID], s.[Name], s.[Exchange], s.[Currency] FROM [Exchanges] e, [Symbols] s WHERE e.[Name] = s.[Exchange] ORDER BY e.[Name]";
                reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    //retreive exchanges data from DB
                    while (reader.Read())
                    {
                        string exchangeName = reader.GetString(0);
                        if (_exchanges.FirstOrDefault(item => item.Name == exchangeName) == null)
                        {
                            _exchanges.Add(new ExchangeSettingsEx() { Name = reader.GetString(0), StartTime = reader.GetTimeSpan(1), EndTime = reader.GetTimeSpan(2), CommonCurrency = reader.GetBoolean(3), Symbols = new List<ExchangeSymbol>() });
                        }
                        _exchanges.First(item => item.Name == exchangeName).Symbols.Add(new ExchangeSymbol() { ID = reader.GetString(4), Symbol = new Symbol() { Name = reader.GetString(5), Exchange = reader.GetString(6), Currency = reader.GetString(7) } });
                    }
                }
                reader.Close();
                command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT * FROM [Currencies]";
                reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    //retreive currencies data from DB
                    while (reader.Read())
                    {
                        _currencies.Add(new Currency() { Name = reader.GetString(0), Multiplier = reader.GetDecimal(1)});
                    }
                }
                reader.Close();
                _exchanges.ForEach(exchange => exchange.Init());
                List<NewOrderRequest> orderRequests = new List<NewOrderRequest>();
                List<Execution> expiriedExecutions = new List<Execution>();
                command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT o.[ID], o.[AccountID], o.[SymbolID], o.[Time], o.[ActivationTime], o.[Side], o.[OrderType], o.[LimitPrice], o.[StopPrice], o.[Quantity], o.[TimeInForce], o.[ExpirationDate], e.[Time], e.[Status], e.[LastPrice], e.[LastQuantity], e.[FilledQuantity], e.[LeaveQuantity], e.[AverrageFillPrice] FROM [Orders] o, [Executions] e WHERE o.ID = e.[OrderID] ";
                reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    //retreive orders and executions data from DB
                    DateTime utcNow = DateTime.UtcNow;
                    while (reader.Read())
                    {
                        string orderID = reader.GetString(0);
                        string orderAccount = reader.GetString(1);
                        string orderSymbolID = reader.GetString(2);                                                
                        DateTime orderTime = reader.GetDateTime(3);
                        orderTime = DateTime.SpecifyKind(orderTime, DateTimeKind.Utc);
                        DateTime orderActivationTime = DateTime.MinValue;
                        if(!reader.IsDBNull(4))
                        {
                            orderActivationTime = reader.GetDateTime(4);
                            orderActivationTime = DateTime.SpecifyKind(orderActivationTime, DateTimeKind.Utc);
                        }
                        Side orderSide = (Side)((byte)reader.GetByte(5));
                        Type orderType = (Type)((byte)reader.GetByte(6));
                        decimal orderLimitPrice = reader.GetDecimal(7);
                        decimal orderStopPrice = reader.GetDecimal(8);
                        decimal orderQuantity = reader.GetDecimal(9);
                        TimeInForce orderTimeInforce = (TimeInForce)((byte)reader.GetByte(10));
                        DateTime orderExpirationDate = reader.GetDateTime(11);
                        orderExpirationDate = DateTime.SpecifyKind(orderExpirationDate, DateTimeKind.Utc);
                        DateTime executionDate = reader.GetDateTime(12);
                        executionDate = DateTime.SpecifyKind(executionDate, DateTimeKind.Utc);
                        Status executionStatus = (Status)((byte)reader.GetByte(13));
                        decimal executionLastPrice = reader.GetDecimal(14);
                        decimal executionLastQuantity = reader.GetDecimal(15);
                        decimal executionFilledQuantity = reader.GetDecimal(16);
                        decimal executionLeaveQuantity = reader.GetDecimal(17);
                        decimal executionAverrageFillPrice = reader.GetDecimal(18);
                        if (executionStatus == Status.Opened || executionStatus == Status.PartialFilled || executionStatus == Status.Activated)
                        {
                            var symbol = _exchanges.SelectMany(e => e.Symbols).Where(s => s.ID == orderSymbolID).FirstOrDefault();
                            if (symbol != null)
                            {
                                //split orders/executions to active and expiried collections
                                if ((orderTimeInforce == TimeInForce.GTD || orderTimeInforce == TimeInForce.DAY) && orderExpirationDate < utcNow)
                                {
                                    expiriedExecutions.Add(new Execution(orderID, executionDate, Status.Expiried, executionLastPrice, executionLastQuantity, executionFilledQuantity, 0, executionLeaveQuantity, ""));
                                }
                                else
                                {
                                    orderRequests.Add(new NewOrderRequest() { ID = orderID, Account = orderAccount, Symbol = symbol.Symbol, Time = orderTime, ActivationTime = orderActivationTime, Side = orderSide, OrderType = orderType, LimitPrice = orderLimitPrice, StopPrice = orderStopPrice, Quantity = orderQuantity, TimeInForce = orderTimeInforce, ExpirationDate = orderExpirationDate });
                                    lock (_tradingLocker)
                                    {
                                        if (orderActivationTime != DateTime.MinValue)
                                        {
                                            _activeOrders.Add(new NewOrderRequest() { ID = orderID, Account = orderAccount, Symbol = symbol.Symbol, Time = orderTime, ActivationTime = orderActivationTime, Side = orderSide, OrderType = orderType, LimitPrice = orderLimitPrice, StopPrice = orderStopPrice, Quantity = orderQuantity, TimeInForce = orderTimeInforce, ExpirationDate = orderExpirationDate });
                                        }
                                        else
                                        {
                                            _stopOorders.Add(new NewOrderRequest() { ID = orderID, Account = orderAccount, Symbol = symbol.Symbol, Time = orderTime, ActivationTime = orderActivationTime, Side = orderSide, OrderType = orderType, LimitPrice = orderLimitPrice, StopPrice = orderStopPrice, Quantity = orderQuantity, TimeInForce = orderTimeInforce, ExpirationDate = orderExpirationDate });
                                        }
                                        _executions.Add(new Execution(orderID, executionDate, executionStatus, executionLastPrice, executionLastQuantity, executionFilledQuantity, executionLeaveQuantity, 0, ""));
                                    }
                                }
                            }
                        }
                    }
                }
                reader.Close();
                //cancel expiried orders
                expiriedExecutions.ForEach(item => 
                {
                    command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE [Executions] SET [Status] = @Status, [LeaveQuantity] = 0, [CancelledQuantity] = @CancelledQuantity WHERE [OrderID] = @OrderID";
                    command.Parameters.AddWithValue("Status", item.Status);
                    command.Parameters.AddWithValue("OrderID", item.OrderID);
                    command.Parameters.AddWithValue("CancelledQuantity", item.CancelledQuantity);
                    command.ExecuteNonQuery();
                });
                transaction.Commit();
                _cache = new Cache(_exchanges, _connectionString);

                _processOrdersThread = new Thread(ProcessRequestHandler);
                _processStopOrdersThread = new Thread(ProcessStopOrdersHandler);
                _processExchangeSessionThread = new Thread(ProcessExchangeSession);
                _started = true;
                _processOrdersThread.Start();                           
                _processStopOrdersThread.Start();               
                _processExchangeSessionThread.Start();
                orderRequests.ForEach(o => ProcessOrderRequest(o));
            }
            catch(Exception ex)
            {
                if (reader != null)
                {
                    reader.Close();
                }
                if (transaction != null)
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch(Exception e)
                    {
                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), e);
                    }
                }
                Stop();
                Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), ex);
                result = ex.Message;
            }
            if (connection.State == System.Data.ConnectionState.Open)
            {
                connection.Close();
            }
            return result;
        }

        //stop exchange engine
        public void Stop()
        {            
            _started = false;
            if (_processOrdersThread != null)
            {
                try
                {
                    _processOrdersThread.Abort();
                }
                catch { }
            }
            if (_processStopOrdersThread != null)
            {
                try
                {
                    _processStopOrdersThread.Abort();
                }
                catch { }
            }
            try
            {
                _processExchangeSessionThread.Abort();
            }
            catch { }
            lock (_tradingLocker)
            {
                _users.Clear();
                _activeOrders.Clear();
                _executions.Clear();
            }
            lock (_exchanges)
            {
                _exchanges.Clear();
            }
            _cache = null;            
        }

        public UserLoginResponse Login(LoginRequest request)
        {
            UserLoginResponse result = new UserLoginResponse() { LoginResult = LoginResult.InternalError, Currencies = new List<string>(), Symbols = new List<Symbol>(), Accounts = new List<Account>(), Executions = new List<Execution>(), Orders = new List<NewOrderRequest>() };
            SqlConnection connection = new SqlConnection(_connectionString);
            try
            {
                //retreive user account data from DB
                connection.Open();
                SqlCommand command = connection.CreateCommand();
                command.CommandText = "SELECT u.[UserName], u.[Password], a.[Name], a.[Balance], a.[Currency] FROM [Users] u, [Accounts] a WHERE u.[UserName] = @UserName AND u.[Password] = @Password AND u.[UserName] = a.[UserName] ORDER BY u.[UserName], a.[Name]";
                command.Parameters.AddWithValue("UserName", request.UserName);
                command.Parameters.AddWithValue("Password", request.Password);
                var reader = command.ExecuteReader();
                if (reader.HasRows)
                {                    
                    while (reader.Read())
                    {
                        result.Accounts.Add(new Account() { Name = reader.GetString(2), Balance = reader.GetDecimal(3), Currency = reader.GetString(4) });
                    }                         
                    reader.Close();
                    //retreive exchanges data from DB
                    command = connection.CreateCommand();
                    command.CommandText = "SELECT * FROM [Symbols]";
                    reader = command.ExecuteReader();
                    if (reader.HasRows)
                    {
                       
                        while (reader.Read())
                        {
                            result.Symbols.Add(new Symbol() { Name = reader.GetString(1), Exchange = reader.GetString(2), Currency = reader.GetString(3) });
                        }
                    }
                    reader.Close();
                    //get currencies
                    command = connection.CreateCommand();
                    command.CommandText = "SELECT [Name] FROM [Currencies]";
                    reader = command.ExecuteReader();
                    if (reader.HasRows)
                    {
                        while (reader.Read())
                        {
                            result.Currencies.Add(reader.GetString(0));
                        }
                    }
                    reader.Close();
                    //retreive user orders and executions data from DB
                    command = connection.CreateCommand();
                    command.CommandText = "SELECT o.[ID], o.[AccountID], o.[SymbolID], o.[Time], o.[Side], o.[OrderType], o.[LimitPrice], o.[StopPrice], o.[Quantity], o.[TimeInForce], o.[ExpirationDate], e.[Time], e.[Status], e.[LastPrice], e.[LastQuantity], e.[FilledQuantity], e.[LeaveQuantity], e.[CancelledQuantity], e.[AverrageFillPrice] FROM [Executions] e INNER JOIN [Orders] o ON e.[OrderID] = o.[ID] INNER JOIN [Accounts] a ON o.[AccountID] = a.[Name] INNER JOIN [Users] u ON u.[UserName] = a.[UserName] WHERE a.[UserName] = @UserName";
                    command.Parameters.AddWithValue("UserName", request.UserName);
                    reader = command.ExecuteReader();
                    if (reader.HasRows)
                    {                        
                        DateTime utcNow = DateTime.UtcNow;
                        while (reader.Read())
                        {
                            string orderID = reader.GetString(0);
                            string orderAccount = reader.GetString(1);
                            string orderSymbolID = reader.GetString(2);  
                            DateTime orderTime = reader.GetDateTime(3);
                            orderTime = DateTime.SpecifyKind(orderTime, DateTimeKind.Utc);
                            Side orderSide = (Side)((byte)reader.GetByte(4));
                            Type orderType = (Type)((byte)reader.GetByte(5));
                            decimal orderLimitPrice = reader.GetDecimal(6);
                            decimal orderStopPrice = reader.GetDecimal(7);
                            decimal orderQuantity = reader.GetDecimal(8);
                            TimeInForce orderTimeInforce = (TimeInForce)((byte)reader.GetByte(9));
                            DateTime orderExpirationDate = reader.GetDateTime(10);
                            orderExpirationDate = DateTime.SpecifyKind(orderExpirationDate, DateTimeKind.Utc);
                            DateTime executionDate = reader.GetDateTime(11);
                            executionDate = DateTime.SpecifyKind(executionDate, DateTimeKind.Utc);
                            Status executionStatus = (Status)((byte)reader.GetByte(12));
                            decimal executionLastPrice = reader.GetDecimal(13);
                            decimal executionLastQuantity = reader.GetDecimal(14);
                            decimal executionFilledQuantity = reader.GetDecimal(15);
                            decimal executionLeaveQuantity = reader.GetDecimal(16);
                            decimal executionCancelledQuantity = reader.GetDecimal(17);
                            decimal executionAverrageFillPrice = reader.GetDecimal(18);
                            var symbol = _exchanges.SelectMany(e => e.Symbols).Where(s => s.ID == orderSymbolID).FirstOrDefault();
                            if (symbol != null)
                            {
                                result.Orders.Add(new NewOrderRequest() { ID = orderID, Account = orderAccount, Symbol = symbol.Symbol, Time = orderTime, Side = orderSide, OrderType = orderType, LimitPrice = orderLimitPrice, StopPrice = orderStopPrice, Quantity = orderQuantity, TimeInForce = orderTimeInforce, ExpirationDate = orderExpirationDate });
                                result.Executions.Add(new Execution(orderID, executionDate, executionStatus, executionLastPrice, executionLastQuantity, executionFilledQuantity, executionLeaveQuantity, executionCancelledQuantity, ""));                                    
                            }
                        }                        
                    }
                    reader.Close();
                    result.LoginResult = LoginResult.OK;
                }
                else
                {
                    reader.Close();
                    result.LoginResult = LoginResult.InvalidCredentials;
                }
            }
            catch
            {
                result.LoginResult = LoginResult.InternalError;                
                result.Symbols.Clear();
                result.Accounts.Clear();
                result.Orders.Clear();
                result.Executions.Clear();
            }
            if (connection.State == System.Data.ConnectionState.Open)
            {
                connection.Close();
            }            
            return result;
        }

        public void AddUser(User user)
        {
            lock (_tradingLocker)
            {
                _users.Add(user);
            }
        }

        public void EditUser(string previousUserName, User user)
        {
            lock (_tradingLocker)
            {
                var previousUser = _users.FirstOrDefault(u => u.UserName == previousUserName);
                if (previousUser != null)
                {
                    //find and remove orders and executions of no longer available accounts
                    var accounts = previousUser.Accounts.Except(user.Accounts, new AccountComparer()).Select(a => a.Name).ToList();
                    var orders = _activeOrders.Where(o => accounts.Contains(o.Account)).Concat(_stopOorders.Where(o => accounts.Contains(o.Account))).Select(o => o.ID).ToList();
                    var executions = _executions.Where(e => orders.Contains(e.OrderID)).ToList();
                    _executions.RemoveAll(e => orders.Contains(e.OrderID));
                    _activeOrders.RemoveAll(o => accounts.Contains(o.Account));
                    _stopOorders.RemoveAll(o => accounts.Contains(o.Account));
                    //edit user data 
                    previousUser.UserName = user.UserName;
                    previousUser.Password = user.Password;
                    previousUser.Accounts = user.Accounts;
                }
                else
                {
                    Log.WriteApplicationInfo("Can't find user to edit in datafeed");
                    _users.Add(user);
                }
            }
        }

        public void DeleteUser(string userName)
        {
            lock (_tradingLocker)
            {
                var previousUser = _users.FirstOrDefault(u => u.UserName == userName);
                if (previousUser != null)
                {
                    //find and remove orders and executions of no longer available accounts
                    var accounts = previousUser.Accounts.Select(a => a.Name).ToList();
                    var orders = _activeOrders.Where(o => accounts.Contains(o.Account)).Concat(_stopOorders.Where(o => accounts.Contains(o.Account))).Select(o => o.ID).ToList();
                    var executions = _executions.Where(e => orders.Contains(e.OrderID));                    
                    _executions.RemoveAll(e => orders.Contains(e.OrderID));
                    _activeOrders.RemoveAll(o => accounts.Contains(o.Account));
                    _stopOorders.RemoveAll(o => accounts.Contains(o.Account));
                    //remove user from users collection
                    _users.RemoveAll(u => u.UserName == userName);
                }
                else
                {
                    Log.WriteApplicationInfo("Can't find user to delete in datafeed");
                }
            }
        }

        public void AddExchange(ExchangeSettingsEx exchange)
        {
            lock (_exchanges)
            {
                exchange.Init();
                _exchanges.Add(exchange);
            }
        }

        public void EditExchange(string previousExchangeName, ExchangeSettingsEx exchange)
        {
            ExchangeSettingsEx previousExchange = null;
            lock (_exchanges)
            {
                previousExchange = _exchanges.FirstOrDefault(e => e.Name == previousExchangeName);
                if (previousExchange != null)
                {
                    previousExchange.StartTime = exchange.StartTime;
                    previousExchange.EndTime = exchange.EndTime;
                    previousExchange.Init();
                    previousExchange.Symbols = exchange.Symbols;
                }
                else
                {
                    Log.WriteApplicationInfo("Can't find exchange to edit in datafeed");
                }
            }
            if (previousExchange != null)
            {
                lock (_tradingLocker)
                {
                    var symbols = previousExchange.Symbols.Select(s => s.Symbol).ToList().Except(exchange.Symbols.Select(s => s.Symbol), new SymbolComparer()).ToList();
                    var orders = _activeOrders.Where(o => symbols.Any(s => s.Equals(o.Symbol))).Concat(_stopOorders.Where(o => symbols.Any(s => s.Equals(o.Symbol)))).Select(o => o.ID).ToList();
                    var executions = _executions.Where(e => orders.Contains(e.OrderID)).ToList();
                    _executions.RemoveAll(e => orders.Contains(e.OrderID));
                    _activeOrders.RemoveAll(o => orders.Contains(o.ID));
                    _stopOorders.RemoveAll(o => orders.Contains(o.ID));
                    _activeOrders.Where(o => exchange.Symbols.Select(s => s.Symbol).Any(s => s.Equals(o.Symbol)) && o.TimeInForce == TimeInForce.DAY).Concat(_stopOorders.Where(o => exchange.Symbols.Select(s => s.Symbol).Any(s => s.Equals(o.Symbol)) && o.TimeInForce == TimeInForce.DAY)).ToList().ForEach(o => o.ExpirationDate = previousExchange.EndDate);
                }
            }
        }

        public void DeleteExchange(string exchangeName)
        {
            ExchangeSettingsEx previousExchange = null;
            lock (_exchanges)
            {
                previousExchange = _exchanges.FirstOrDefault(e => e.Name == exchangeName);
                if (previousExchange != null)
                {   
                    _exchanges.Remove(previousExchange);
                }
                else
                {
                    Log.WriteApplicationInfo("Can't find exchange to delete in datafeed");
                }
            }
            if (previousExchange != null)
            {
                lock (_tradingLocker)
                {
                    var symbols = previousExchange.Symbols.Select(s => s.Symbol.Name).ToList();
                    var orders = _activeOrders.Where(o => symbols.Contains(o.Symbol.Name)).Concat(_stopOorders.Where(o => symbols.Contains(o.Symbol.Name))).Select(o => o.ID).ToList();
                    var executions = _executions.Where(e => orders.Contains(e.OrderID)).ToList();
                    _executions.RemoveAll(e => orders.Contains(e.OrderID));
                    _activeOrders.RemoveAll(o => orders.Contains(o.ID));
                    _stopOorders.RemoveAll(o => orders.Contains(o.ID));
                }
            }
        }

        public void SetCurrencies(List<Currency> currencies)
        {
            lock (_tradingLocker)
            {
                var oldCurrenies = _currencies.Where(oc => currencies.FirstOrDefault(c => c.Name == oc.Name) == null).Select(c => c.Name);
                var existingCurrenies = _currencies.Where(oc => currencies.FirstOrDefault(c => c.Name == oc.Name) != null).Select(c => c.Name);
                _currencies.RemoveAll(c => oldCurrenies.Contains(c.Name));
                currencies.ForEach(c =>
                {
                    if (!existingCurrenies.Contains(c.Name))
                    {
                        _currencies.Add(c);
                    }
                });
            }
        }                

        public Tick GetLastTick(Symbol symbol, string currency = "")
        {
            var lastTick = _cache == null ? null : _cache.GetLastTick(symbol);
            var result = new Tick(symbol, DateTime.MinValue, 0, 0) { Currency = currency };
            if (lastTick != null)
            {
                result.Time = lastTick.Time;
                result.Bid = lastTick.Bid;
                result.BidSize = lastTick.BidSize;
                result.Ask = lastTick.Ask;
                result.AskSize= lastTick.AskSize;
                result.Price = lastTick.Price;
                result.Volume = lastTick.Volume;
                if (!string.IsNullOrEmpty(currency))
                {
                    var currencyMultiplier = GetCurrencyMultiplier(currency);
                    var baseMultiplier = GetCurrencyMultiplier(symbol.Currency);
                    if (currencyMultiplier != 0 && baseMultiplier != 0)
                    {
                        var multiplier = baseMultiplier / currencyMultiplier;
                        result.Bid *= multiplier;
                        result.Ask *= multiplier;
                        result.Price *= multiplier;
                    }
                    else
                    {
                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Currency {0} or {1} does not exist", symbol.Currency, currency)));
                    }
                }
            }
            return result;
        }

        public List<Bar> GetHistory(HistoryParameters request)
        {
            var result = _cache.GetHistory(request);
            if (!string.IsNullOrEmpty(request.Currency))
            {
                var currencyMultiplier = GetCurrencyMultiplier(request.Currency);
                var baseMultiplier = GetCurrencyMultiplier(request.Symbol.Currency);
                if (currencyMultiplier != 0 && baseMultiplier != 0)
                {
                    var multiplier = baseMultiplier / currencyMultiplier;
                    result.ForEach(b =>
                    {
                        b.Open *= multiplier;
                        b.High *= multiplier;
                        b.Low *= multiplier;
                        b.Close *= multiplier;
                    });
                }
                else
                {
                    Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Currency {0} or {1} does not exist", request.Symbol.Currency, request.Currency)));
                }
            }           
            return result;
        }
                               
        public void ProcessRequest(object request)
        {
            //add user request to queue and set event to process current events queue
            lock(_requests)
            {
                _requests.Enqueue(request);
            }
            _processOrdersEvent.Set();
        }

        private decimal GetCurrencyMultiplier(string convertedCurrency)
        {
            decimal result = 0;
            lock (_currencies)
            {
                var currency = _currencies.FirstOrDefault(c => c.Name == convertedCurrency);
                if (currency != null)
                {
                    result = currency.Multiplier;
                }
            }
            return result;
        }
               
        private void ProcessRequestHandler()
        {
            while(_started)
            {
                _processOrdersEvent.WaitOne();
                _processOrdersEvent.Reset();
                while(_requests.Count > 0)
                {
                    object request = null;
                    lock(_requests)
                    {
                        request = _requests.Dequeue();
                    }
                    if(request != null)
                    {
                        //process new order request
                        if(request as NewOrderRequest != null)
                        {
                            NewOrderRequest orderRequest = (NewOrderRequest)request;
                            DateTime transactionTime = DateTime.UtcNow;
                            //validate order parameters
                            string error = ValidateNewOrderRequest(orderRequest);
                            if (string.IsNullOrEmpty(error))
                            {
                                ExchangeSymbol symbol = null;
                                lock (_exchanges)
                                {
                                    var exchangeSettings = _exchanges.FirstOrDefault(e => e.Name == orderRequest.Symbol.Exchange);
                                    if (exchangeSettings != null)
                                    {
                                        //set order expiration date acording to order TimeInForce and ExpirationDate
                                        if (orderRequest.TimeInForce == TimeInForce.DAY || orderRequest.TimeInForce == TimeInForce.AON)
                                        {
                                            orderRequest.ExpirationDate = exchangeSettings.EndDate;
                                        }
                                        else if (orderRequest.TimeInForce == TimeInForce.GTC)
                                        {
                                            orderRequest.ExpirationDate = _UNIX_START;
                                        }
                                        symbol = exchangeSettings.Symbols.FirstOrDefault(s => s.Symbol.Equals(orderRequest.Symbol));
                                        if (symbol == null)
                                        {
                                            error = "Invalid symbol";
                                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Invalid exchange, ID {0}", orderRequest.ID)));
                                        }
                                    }
                                    else
                                    {
                                        error = "Internal error on server";
                                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Invalid symbol, ID {0}", orderRequest.ID)));
                                    }
                                }
                                if (string.IsNullOrEmpty(error))
                                {
                                    if (orderRequest.OrderType == Type.Market || orderRequest.OrderType == Type.Limit)
                                    {
                                        orderRequest.ActivationTime = transactionTime;
                                    }
                                    orderRequest.ExpirationDate = orderRequest.ExpirationDate.ToUniversalTime();
                                    //save order and execution in DB in a single transaction
                                    SqlConnection connection = new SqlConnection(_connectionString);
                                    SqlTransaction transaction = null;
                                    try
                                    {
                                        connection.Open();
                                        transaction = connection.BeginTransaction();
                                        SqlCommand command = connection.CreateCommand();
                                        command.Transaction = transaction;
                                        command.CommandText = "INSERT INTO [Orders] VALUES (@ID, @AccountID, @SymbolID, @Time, @ActivationTime, @Side, @OrderType, @LimitPrice, @StopPrice, @Quantity, @TimeInForce, @ExpirationDate)";
                                        command.Parameters.AddWithValue("ID", orderRequest.ID);
                                        command.Parameters.AddWithValue("AccountID", orderRequest.Account);
                                        command.Parameters.AddWithValue("SymbolID",symbol.ID);
                                        command.Parameters.AddWithValue("Time", transactionTime);
                                        if (orderRequest.ActivationTime == DateTime.MinValue)
                                        {
                                            command.Parameters.AddWithValue("ActivationTime", DBNull.Value);
                                        }
                                        else
                                        {
                                            command.Parameters.AddWithValue("ActivationTime", orderRequest.ActivationTime);
                                        }                                        
                                        command.Parameters.AddWithValue("Side", (byte)orderRequest.Side);
                                        command.Parameters.AddWithValue("OrderType", (byte)orderRequest.OrderType);
                                        command.Parameters.AddWithValue("LimitPrice", orderRequest.LimitPrice);
                                        command.Parameters.AddWithValue("StopPrice", orderRequest.StopPrice);
                                        command.Parameters.AddWithValue("Quantity", orderRequest.Quantity);
                                        command.Parameters.AddWithValue("TimeInForce", orderRequest.TimeInForce);
                                        command.Parameters.AddWithValue("ExpirationDate", orderRequest.ExpirationDate);
                                        command.ExecuteNonQuery();     
                                   
                                        command = connection.CreateCommand();
                                        command.Transaction = transaction;
                                        command.CommandText = "INSERT INTO [Executions] VALUES (@OrderID, @Time, @Status, 0, 0, 0, @LeaveQuantity, 0, 0)";
                                        command.Parameters.AddWithValue("OrderID", orderRequest.ID);
                                        command.Parameters.AddWithValue("Time", transactionTime);
                                        command.Parameters.AddWithValue("Status", (byte)Status.Opened);
                                        command.Parameters.AddWithValue("LeaveQuantity", orderRequest.Quantity);
                                        command.ExecuteNonQuery();
                                        transaction.Commit();
                                    }
                                    catch (Exception ex)
                                    {
                                        if (transaction != null)
                                        {
                                            transaction.Rollback();
                                        }
                                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), ex);
                                        error = "Internal error on server";
                                    }
                                    if (connection.State == System.Data.ConnectionState.Open)
                                    {
                                        connection.Close();
                                    }
                                    if (string.IsNullOrEmpty(error))
                                    {
                                        var execution = new Execution(orderRequest.ID, transactionTime, Status.Opened, 0, 0, 0, orderRequest.Quantity, 0, error);
                                        FireExecution(orderRequest.Account, execution);
                                        lock (_tradingLocker)
                                        {
                                            //store execution in local collection
                                            _executions.Add(execution);
                                            //store order in local collection
                                            if (orderRequest.ActivationTime != DateTime.MinValue)
                                            {
                                                _activeOrders.Add(orderRequest);
                                            }
                                            else
                                            {
                                                _stopOorders.Add(orderRequest);
                                            }
                                        }
                                        if (orderRequest.ActivationTime != DateTime.MinValue)
                                        {
                                            //process request according to matching rules
                                            ProcessOrderRequest(orderRequest);
                                        }
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(error))
                            {
                                //some error occurred, send message to user
                                FireExecution(orderRequest.Account, new Execution(orderRequest.ID, transactionTime, Status.Rejected, 0, 0, 0, 0, orderRequest.Quantity, error));
                            }
                        }
                        //process modify order request
                        else if (request as ModifyOrderRequest != null)
                        {
                            ModifyOrderRequest modifyOrderRequest = (ModifyOrderRequest)request;
                            DateTime transactionTime = DateTime.UtcNow;
                            string account = "";
                            //get order account
                            lock (_tradingLocker)
                            {
                                var orderToModify = _activeOrders.FirstOrDefault(o => o.ID == modifyOrderRequest.ID);
                                if (orderToModify == null)
                                {
                                    orderToModify = _stopOorders.FirstOrDefault(o => o.ID == modifyOrderRequest.ID);
                                }
                                if (orderToModify != null)
                                {
                                    account = orderToModify.Account;
                                }
                            }
                            //validate order parameters
                            string error = ValidateModifyOrderRequest(modifyOrderRequest);
                            if (string.IsNullOrEmpty(error))
                            {
                                string exchange = "";
                                lock (_tradingLocker)
                                {
                                    var orderToModify = _activeOrders.FirstOrDefault(o => o.ID == modifyOrderRequest.ID);
                                    if (orderToModify == null)
                                    {
                                        orderToModify = _stopOorders.FirstOrDefault(o => o.ID == modifyOrderRequest.ID);
                                    }
                                    if (orderToModify != null)
                                    {
                                        exchange = orderToModify.Symbol.Exchange;
                                    }
                                }
                                if(!string.IsNullOrEmpty(exchange))
                                {
                                    lock (_exchanges)
                                    {
                                        var exchangeSettings = _exchanges.FirstOrDefault(e => e.Name == exchange);
                                        if (exchangeSettings != null)
                                        {
                                            //set order expiration date acording to order TimeInForce and ExpirationDate
                                            if (modifyOrderRequest.TimeInForce == TimeInForce.DAY || modifyOrderRequest.TimeInForce == TimeInForce.AON)
                                            {
                                                modifyOrderRequest.ExpirationDate = exchangeSettings.EndDate;
                                            }
                                            else if (modifyOrderRequest.TimeInForce == TimeInForce.GTC)
                                            {
                                                modifyOrderRequest.ExpirationDate = _UNIX_START;
                                            }
                                        }
                                        else
                                        {
                                            error = "Internal error on server";
                                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Invalid order to modify, ID {0}", modifyOrderRequest.ID)));
                                        }
                                    }
                                }
                                else
                                {
                                    error = "Internal error on server";
                                    Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Invalid order ID, ID {0}", modifyOrderRequest.ID)));
                                }
                                if (string.IsNullOrEmpty(error))
                                {
                                    DateTime activationTime = DateTime.MinValue;
                                    //modify order activation time if needed
                                    if (modifyOrderRequest.OrderType == Type.Market || modifyOrderRequest.OrderType == Type.Limit)
                                    {
                                        activationTime = transactionTime;
                                    }
                                    NewOrderRequest order = null;
                                    lock (_tradingLocker)
                                    {
                                        order = _activeOrders.FirstOrDefault(o => o.ID == modifyOrderRequest.ID);
                                        if (order == null)
                                        {
                                            order = _stopOorders.FirstOrDefault(o => o.ID == modifyOrderRequest.ID);
                                        }
                                        if (order != null)
                                        {
                                            var execution = _executions.FirstOrDefault(e => e.OrderID == modifyOrderRequest.ID);
                                            modifyOrderRequest.ExpirationDate = modifyOrderRequest.ExpirationDate.ToUniversalTime();
                                            if (execution != null)
                                            {
                                                //update order and execution in DB in a single transaction
                                                SqlConnection connection = new SqlConnection(_connectionString);
                                                SqlTransaction transaction = null;
                                                try
                                                {
                                                    connection.Open();
                                                    transaction = connection.BeginTransaction();
                                                    SqlCommand command = connection.CreateCommand();
                                                    command.Transaction = transaction;
                                                    command.CommandText = "UPDATE [Orders] SET [Time] = @Time, [ActivationTime] = @ActivationTime,[OrderType] = @OrderType, [LimitPrice] = @LimitPrice, [StopPrice] = @StopPrice, [Quantity] = @Quantity, [TimeInForce] = @TimeInForce, [ExpirationDate] = @ExpirationDate WHERE [ID] = @ID";
                                                    command.Parameters.AddWithValue("ID", modifyOrderRequest.ID);
                                                    command.Parameters.AddWithValue("Time", transactionTime);
                                                    if (activationTime == DateTime.MinValue)
                                                    {
                                                        command.Parameters.AddWithValue("ActivationTime", DBNull.Value);
                                                    }
                                                    else
                                                    {
                                                        command.Parameters.AddWithValue("ActivationTime", activationTime);
                                                    }
                                                    command.Parameters.AddWithValue("OrderType", (byte)modifyOrderRequest.OrderType);
                                                    command.Parameters.AddWithValue("LimitPrice", modifyOrderRequest.LimitPrice);
                                                    command.Parameters.AddWithValue("StopPrice", modifyOrderRequest.StopPrice);
                                                    command.Parameters.AddWithValue("Quantity", modifyOrderRequest.Quantity + execution.FilledQuantity);
                                                    command.Parameters.AddWithValue("TimeInForce", modifyOrderRequest.TimeInForce);
                                                    command.Parameters.AddWithValue("ExpirationDate", modifyOrderRequest.ExpirationDate);
                                                    command.ExecuteNonQuery();

                                                    command = connection.CreateCommand();
                                                    command.Transaction = transaction;
                                                    command.CommandText = "UPDATE [Executions] SET [Time] = @Time, [LeaveQuantity] = @LeaveQuantity, [Status] = @Status WHERE [OrderID] = @ID";
                                                    command.Parameters.AddWithValue("ID", modifyOrderRequest.ID);
                                                    command.Parameters.AddWithValue("Time", transactionTime);
                                                    command.Parameters.AddWithValue("LeaveQuantity", modifyOrderRequest.Quantity);
                                                    command.Parameters.AddWithValue("Status", execution.Status == Status.Activated ? (byte)Status.Opened : (byte)execution.Status);
                                                    command.ExecuteNonQuery();
                                                    transaction.Commit();
                                                }
                                                catch (Exception ex)
                                                {
                                                    if (transaction != null)
                                                    {
                                                        transaction.Rollback();
                                                    }
                                                    Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), ex);
                                                    error = "Internal error on server";
                                                }
                                                if (connection.State == System.Data.ConnectionState.Open)
                                                {
                                                    connection.Close();
                                                }
                                                if (string.IsNullOrEmpty(error))
                                                {
                                                    //update execution in local collection
                                                    execution.Time = transactionTime;
                                                    execution.LeaveQuantity = modifyOrderRequest.Quantity;
                                                    if(execution.Status == Status.Activated)
                                                    {
                                                        execution.Status = Status.Opened;
                                                    }
                                                    FireExecution(order.Account, execution);
                                                    //update order in local collection
                                                    order.OrderType = modifyOrderRequest.OrderType;
                                                    order.StopPrice = modifyOrderRequest.StopPrice;
                                                    order.LimitPrice = modifyOrderRequest.LimitPrice;
                                                    order.Quantity = modifyOrderRequest.Quantity;
                                                    order.TimeInForce = modifyOrderRequest.TimeInForce;
                                                    order.ExpirationDate = modifyOrderRequest.ExpirationDate;
                                                    order.Time = transactionTime;
                                                    order.ActivationTime = activationTime;
                                                    //update market/limit orders collection and stop orders collection if needed
                                                    _activeOrders.Remove(order);
                                                    _stopOorders.Remove(order);
                                                    if (order.ActivationTime != DateTime.MinValue)
                                                    {
                                                        _activeOrders.Add(order);
                                                    }
                                                    else
                                                    {
                                                        _stopOorders.Add(order);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                error = "Internal error on server";
                                                Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Execution for order does not exist, ID {0}", modifyOrderRequest.ID)));
                                            }
                                        }
                                        else
                                        {
                                            error = "Internal error on server";
                                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Invalid order ID, ID {0}", modifyOrderRequest.ID)));
                                        }
                                    }
                                    if (order != null)
                                    {
                                        User user = _users.FirstOrDefault(u => u.Accounts.FirstOrDefault(a => a.Name == order.Account) != null);
                                        if (user != null && OnMessageResponse != null)
                                        {
                                            OnMessageResponse(user.UserName, modifyOrderRequest.MessageID, error);
                                        }
                                        else
                                        {
                                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception("Invalid user"));
                                        }
                                        //process request according to matching rules
                                        if (activationTime != DateTime.MinValue)
                                        {
                                            ProcessOrderRequest(order);
                                        }
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(error))
                            {
                                Status status = Status.Rejected;
                                lock (_tradingLocker)
                                {
                                    var execution = _executions.FirstOrDefault(e => e.OrderID == modifyOrderRequest.ID);
                                    if (execution != null)
                                    {
                                        status = execution.Status;
                                    }
                                }
                                //some error occurred, send message to user
                                FireExecution(account, new Execution(modifyOrderRequest.ID, transactionTime, status, 0, 0, 0, 0, 0, string.Format("Modify rejected: {0}", error)));
                            }
                        }
                        else if (request as CancelOrderRequest != null)
                        {
                            //process cancel order
                            CancelOrder(request as CancelOrderRequest);
                        }
                        else
                        {
                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception("Unsupported message type received"));
                        }
                    }
                }
            }
        }

        private void CancelOrder(CancelOrderRequest request, bool validate = true)
        {
            string account = "";
            //get order account
            lock (_tradingLocker)
            {
                var orderToCancel = _activeOrders.FirstOrDefault(o => o.ID == request.ID);
                if (orderToCancel == null)
                {
                    orderToCancel = _stopOorders.FirstOrDefault(o => o.ID == request.ID);
                }
                if (orderToCancel != null)
                {
                    account = orderToCancel.Account;
                }
            }
            DateTime transactionTime = DateTime.UtcNow;
            //validate order parameters if order is canceled by user, not by exchange
            string error = validate ? ValidateCancelOrderRequest(request) : "";
            if (string.IsNullOrEmpty(error))
            {
                lock (_tradingLocker)
                {
                    NewOrderRequest order = _activeOrders.FirstOrDefault(o => o.ID == request.ID);
                    if (order == null)
                    {
                        order = _stopOorders.FirstOrDefault(o => o.ID == request.ID);
                    }
                    if (order != null)
                    {                        
                        var execution = _executions.FirstOrDefault(e => e.OrderID == request.ID);
                        if (execution != null)
                        {
                            account = order.Account;
                            //update execution in database
                            SqlConnection connection = new SqlConnection(_connectionString);
                            try
                            {
                                connection.Open();
                                SqlCommand command = connection.CreateCommand();
                                command.CommandText = "UPDATE [Executions] SET [Status] = @Status, [LeaveQuantity] = 0, [CancelledQuantity] = @CancelledQuantity WHERE [OrderID] = @ID";
                                command.Parameters.AddWithValue("ID", request.ID);
                                command.Parameters.AddWithValue("CancelledQuantity", execution.LeaveQuantity);
                                command.Parameters.AddWithValue("Status", (byte)Status.Cancelled);
                                command.ExecuteNonQuery();
                            }
                            catch (Exception ex)
                            {
                                Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), ex);
                                error = ex.Message;
                            }
                            if (connection.State == System.Data.ConnectionState.Open)
                            {
                                connection.Close();
                            }
                            if (string.IsNullOrEmpty(error))
                            {
                                execution.Status = Status.Cancelled;
                                execution.CancelledQuantity = execution.LeaveQuantity;
                                execution.LeaveQuantity = 0;
                                //remove order and execution from local collection
                                if (order.ActivationTime != DateTime.MinValue)
                                {
                                    _activeOrders.Remove(order);
                                }
                                else
                                {
                                    _stopOorders.Remove(order);
                                }
                                _executions.Remove(execution);
                                FireExecution(account, execution);
                            }
                        }
                        else
                        {
                            error = "Internal error on server";
                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Execution for order does not exist, ID {0}", request.MessageID)));
                        }                        
                    }
                    else
                    {
                        error = "Internal error on server";
                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Invalid order ID, ID {0}", request.MessageID)));
                    }
                }
            }  
            if (!string.IsNullOrEmpty(error))
            {
                Status status = Status.Rejected;
                lock (_tradingLocker)
                {
                    var execution = _executions.FirstOrDefault(e => e.OrderID == request.ID);
                    if (execution != null)
                    {
                        status = execution.Status;
                    }
                }
                //some error occurred, send message to user
                FireExecution(account, new Execution(request.ID, transactionTime, status, 0, 0, 0, 0, 0, string.Format("Cancel rejected: {0}", error)));
            }
        }        

        private string ValidateNewOrderRequest(NewOrderRequest request)
        {
            string error = "";
            //check is order id unique
            SqlConnection connection = new SqlConnection(_connectionString);
            SqlDataReader reader = null;
            try
            {
                connection.Open();
                SqlCommand command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM [Orders] WHERE [ID] = @ID";
                command.Parameters.AddWithValue("ID", request.ID);
                reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    error = "Invalid order ID. Must be unique.";
                }
            }
            catch (Exception ex)
            {
                Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), ex);
                error = "Internal error on server";
            }
            if (reader != null)
            {
                reader.Close();
            }
            if (connection.State == System.Data.ConnectionState.Open)
            {
                connection.Close();
            }
            if (string.IsNullOrEmpty(error))
            {
                lock (_exchanges)
                {
                    //check order exchange
                    var exchangeSettings = _exchanges.FirstOrDefault(e => e.Name == request.Symbol.Exchange);
                    if (exchangeSettings != null)
                    {
                        DateTime utcNow = DateTime.UtcNow;
                        //check is an order sent in exchange trading hours
                        if (utcNow >= exchangeSettings.StartDate && utcNow < exchangeSettings.EndDate)
                        {
                            if (!string.IsNullOrEmpty(request.ID))
                            {
                                lock (_tradingLocker)
                                {
                                    //find user in local collection
                                    if (_users.FirstOrDefault(u => u.Accounts.FirstOrDefault(a => a.Name.ToUpper() == request.Account.ToUpper()) != null) != null)
                                    {
                                        //check is the symbol valid
                                        if (exchangeSettings.Symbols.FirstOrDefault(item => item.Symbol.Equals(request.Symbol)) != null)
                                        {
                                            //check order stop/limit price according to order type
                                            if ((request.OrderType == Type.Market && request.LimitPrice == 0 && request.StopPrice == 0)
                                                || (request.OrderType == Type.Limit && request.LimitPrice != 0 && request.StopPrice == 0)
                                                || (request.OrderType == Type.Stop && request.StopPrice != 0 && request.LimitPrice == 0)
                                                || (request.OrderType == Type.StopLimit && request.StopPrice != 0 && request.LimitPrice != 0))
                                            {
                                                //check order quantity
                                                if (request.Quantity > 0)
                                                {
                                                    //check order TimeInForce and expiration date
                                                    if (!(request.TimeInForce == TimeInForce.GTD && (request.ExpirationDate == DateTime.MinValue || request.ExpirationDate.ToUniversalTime() <= DateTime.UtcNow)))
                                                    {
                                                        var userAccount = _users.FirstOrDefault(u => u.Accounts.FirstOrDefault(a => a.Name == request.Account) != null).Accounts.FirstOrDefault(a => a.Name == request.Account);
                                                        //check order user/account
                                                        if (userAccount != null)
                                                        {
                                                            if (_currencies.FirstOrDefault(item => item.Name == userAccount.Currency) != null)
                                                            {
                                                                Tick lastTick = GetLastTick(request.Symbol);
                                                                decimal stopPrice = 0;
                                                                decimal limitPrice = 0;
                                                                if (exchangeSettings.CommonCurrency)
                                                                {
                                                                    limitPrice = request.LimitPrice * GetCurrencyMultiplier(userAccount.Currency);
                                                                    stopPrice = request.StopPrice * GetCurrencyMultiplier(userAccount.Currency);
                                                                }
                                                                else
                                                                {
                                                                    limitPrice = request.LimitPrice;
                                                                    stopPrice = request.StopPrice;
                                                                }                                                                
                                                                //check stop order price value
                                                                if (lastTick.Price > 0 && (request.OrderType == Type.Stop || request.OrderType == Type.StopLimit))
                                                                {
                                                                    if ((request.Side == Side.Buy && stopPrice <= lastTick.Price) || (request.Side == Side.Sell && stopPrice >= lastTick.Price))
                                                                    {
                                                                        error = string.Format("Invalid order stop price, ID {0}", request.ID);
                                                                        Log.WriteApplicationInfo(error);
                                                                    }
                                                                }
                                                                if (string.IsNullOrEmpty(error))
                                                                {
                                                                    //check user balance for buy orders
                                                                    if (request.Side == Side.Buy)
                                                                    {
                                                                        decimal balance = 0;
                                                                        if (exchangeSettings.CommonCurrency)
                                                                        {
                                                                            balance = userAccount.Balance * GetCurrencyMultiplier(userAccount.Currency);
                                                                        }
                                                                        else
                                                                        {
                                                                            balance = userAccount.Balance * (GetCurrencyMultiplier(userAccount.Currency) / (GetCurrencyMultiplier(request.Symbol.Currency)));
                                                                        }
                                                                        if (request.OrderType == Type.Market)
                                                                        {
                                                                            if (lastTick != null)
                                                                            {
                                                                                balance -= (lastTick.Price * request.Quantity);
                                                                            }
                                                                        }
                                                                        else if (request.OrderType == Type.Limit)
                                                                        {
                                                                            balance -= (limitPrice * request.Quantity);
                                                                        }
                                                                        else if (request.OrderType == Type.Stop)
                                                                        {
                                                                            balance -= (stopPrice * request.Quantity);
                                                                        }
                                                                        else if (request.OrderType == Type.StopLimit)
                                                                        {
                                                                            balance -= (limitPrice * request.Quantity);
                                                                        }
                                                                        if (balance < 0)
                                                                        {
                                                                            error = string.Format("There is no enought funds to place order, ID {0}", request.ID);
                                                                            Log.WriteApplicationInfo(error);
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                            else
                                                            {
                                                                error = string.Format("Invalid currency, ID {0}", request.ID);
                                                                Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                                                            }
                                                        }
                                                        else
                                                        {
                                                            error = string.Format("Invalid order account for order, ID {0}", request.ID);
                                                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                                                        }
                                                    }
                                                    else
                                                    {
                                                        error = string.Format("Invalid order expiration date, ID {0}", request.ID);
                                                        Log.WriteApplicationInfo(error);
                                                    }
                                                }
                                                else
                                                {
                                                    error = string.Format("Invalid order quantity, ID {0}", request.ID);
                                                    Log.WriteApplicationInfo(error);
                                                }
                                            }
                                            else
                                            {
                                                error = string.Format("Invalid order price, ID {0}", request.ID);
                                                Log.WriteApplicationInfo(error);
                                            }
                                        }
                                        else
                                        {
                                            error = string.Format("Invalid symbol, ID {0}", request.ID);
                                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                                        }
                                    }
                                    else
                                    {
                                        error = string.Format("Invalid account, ID {0}", request.ID);
                                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                                    }
                                }
                            }
                            else
                            {
                                error = string.Format("Invalid order ID, ID {0}", request.ID);
                                Log.WriteApplicationInfo(error);
                            }
                        }
                        else
                        {
                            error = string.Format("Exchange is closed, ID {0}", request.ID);
                            Log.WriteApplicationInfo(error);
                        }
                    }
                    else
                    {
                        error = string.Format("Invalid exchange, ID {0}", request.ID);
                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                    }
                }
            }
            return error;
        }

        private string ValidateModifyOrderRequest(ModifyOrderRequest request)
        {
            string error = "";
            if (!string.IsNullOrEmpty(request.ID))
            {
                //check order stop/limit price according to order type
                if ((request.OrderType == Type.Market && request.LimitPrice == 0 && request.StopPrice == 0)
                    || (request.OrderType != Type.Market) && ((request.OrderType == Type.Limit && request.LimitPrice != 0 && request.StopPrice == 0)
                    || (request.OrderType == Type.Stop && request.StopPrice != 0 && request.LimitPrice == 0)
                    || (request.OrderType == Type.StopLimit && request.StopPrice != 0 && request.LimitPrice != 0)))
                {
                    //check order quantity
                    if (request.Quantity > 0)
                    {
                        //check order TimeInForce and expiration date
                        if (!(request.TimeInForce == TimeInForce.GTD && (request.ExpirationDate == DateTime.MinValue || request.ExpirationDate.ToUniversalTime() <= DateTime.UtcNow)))
                        {
                            lock (_tradingLocker)
                            {                                
                                NewOrderRequest orderModifying = _activeOrders.FirstOrDefault(o => o.ID == request.ID);
                                if (orderModifying == null)
                                {
                                    orderModifying = _stopOorders.FirstOrDefault(o => o.ID == request.ID);
                                }
                                //find order to modify in local collection
                                if (orderModifying != null)
                                {
                                    //check order user/account
                                    var userAccount = _users.FirstOrDefault(u => u.Accounts.FirstOrDefault(a => a.Name == orderModifying.Account) != null).Accounts.FirstOrDefault(a => a.Name == orderModifying.Account);
                                    if (userAccount != null)
                                    {
                                        //check order execution
                                        var execution = _executions.FirstOrDefault(e => e.OrderID == request.ID);
                                        if (execution != null)
                                        {                                            
                                            lock (_exchanges)
                                            {
                                                //check order exchange
                                                var exchangeSettings = _exchanges.FirstOrDefault(e => e.Name == orderModifying.Symbol.Exchange);
                                                if (exchangeSettings != null)
                                                {
                                                    DateTime utcNow = DateTime.UtcNow;
                                                    //check is an order sent in exchange trading hours
                                                    if (utcNow >= exchangeSettings.StartDate && utcNow < exchangeSettings.EndDate)
                                                    {
                                                        var lastTick = GetLastTick(orderModifying.Symbol);
                                                        decimal stopPrice = 0;
                                                        decimal limitPrice = 0;
                                                        if (exchangeSettings.CommonCurrency)
                                                        {
                                                            limitPrice = request.LimitPrice * GetCurrencyMultiplier(userAccount.Currency);
                                                            stopPrice = request.StopPrice * GetCurrencyMultiplier(userAccount.Currency);
                                                        }
                                                        else
                                                        {
                                                            limitPrice = request.LimitPrice;
                                                            stopPrice = request.StopPrice;
                                                        } 
                                                        //check stop order price value
                                                        if (lastTick.Price > 0 && (request.OrderType == Type.Stop || request.OrderType == Type.StopLimit))
                                                        {
                                                            if ((orderModifying.Side == Side.Buy && stopPrice <= lastTick.Price) || (orderModifying.Side == Side.Sell && stopPrice >= lastTick.Price))
                                                            {
                                                                error = string.Format("Invalid order stop price, ID {0}", request.ID);
                                                                Log.WriteApplicationInfo(error);
                                                            }
                                                        }
                                                        if (string.IsNullOrEmpty(error))
                                                        {
                                                            //check user balance for buy orders
                                                            if (orderModifying.Side == Side.Buy)
                                                            {
                                                                decimal balance = 0;
                                                                if (exchangeSettings.CommonCurrency)
                                                                {
                                                                    balance = userAccount.Balance * GetCurrencyMultiplier(userAccount.Currency);
                                                                }
                                                                else
                                                                {
                                                                    balance = userAccount.Balance * (GetCurrencyMultiplier(userAccount.Currency) / (GetCurrencyMultiplier(orderModifying.Symbol.Currency)));
                                                                }
                                                                if (request.OrderType == Type.Market)
                                                                {
                                                                    if (lastTick != null)
                                                                    {
                                                                        balance -= (lastTick.Price * request.Quantity);
                                                                    }
                                                                }
                                                                else if (request.OrderType == Type.Limit)
                                                                {
                                                                    balance -= (request.LimitPrice * request.Quantity);
                                                                }
                                                                else if (request.OrderType == Type.Stop)
                                                                {
                                                                    balance -= (request.StopPrice * request.Quantity);
                                                                }
                                                                else if (request.OrderType == Type.StopLimit)
                                                                {
                                                                    balance -= (request.LimitPrice * request.Quantity);
                                                                }
                                                                if (balance < 0)
                                                                {
                                                                    error = string.Format("There is no enought funds to modify order, ID {0}", request.ID);
                                                                    Log.WriteApplicationInfo(error);
                                                                }
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        error = string.Format("Exchange is closed, ID {0}", request.ID);
                                                        Log.WriteApplicationInfo(error);
                                                    }
                                                }
                                                else
                                                {
                                                    error = string.Format("Invalid exchange, ID {0}", request.ID);
                                                    Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                                                }
                                            } 
                                        }
                                        else
                                        {
                                            error = string.Format("Execution for order does not exist, ID {0}", request.ID);
                                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                                        }                                        
                                    }
                                    else
                                    {
                                        error = string.Format("Invalid order account, ID {0}", request.ID);
                                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                                    }
                                }
                                else
                                {
                                    error = string.Format("Invalid order ID, ID {0}", request.ID);
                                    Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                                }                                
                            }
                        }
                        else
                        {
                            error = string.Format("Invalid order expiration date, ID {0}", request.ID);
                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                        }
                    }
                    else
                    {
                        error = string.Format("Invalid order quantity, ID {0}", request.ID);
                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                    }
                }
                else
                {
                    error = string.Format("Invalid order price, ID {0}", request.ID);
                    Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                }
            }
            else
            {
                error = string.Format("Invalid order ID, ID {0}", request.ID);
                Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
            }
            return error;
        }

        private string ValidateCancelOrderRequest(CancelOrderRequest request)
        {
            string error = "";
            lock (_tradingLocker)
            {
                //find order to cancel in local collection
                NewOrderRequest order = _activeOrders.FirstOrDefault(o => o.ID == request.ID);
                if (order == null)
                {
                    order = _stopOorders.FirstOrDefault(o => o.ID == request.ID);
                }
                if (order != null)
                {
                    //check order execution
                    var execution = _executions.FirstOrDefault(e => e.OrderID == request.ID);
                    if (execution != null)
                    {   
                        lock (_exchanges)
                        {
                            //check order exchange
                            var exchangeSettings = _exchanges.FirstOrDefault(e => e.Name == order.Symbol.Exchange);
                            if (exchangeSettings != null)
                            {
                                DateTime utcNow = DateTime.UtcNow;
                                if (utcNow < exchangeSettings.StartDate || utcNow >= exchangeSettings.EndDate)
                                {
                                    error = string.Format("Exchange is closed, ID {0}", request.ID);
                                    Log.WriteApplicationInfo(error);
                                }
                            }
                            else
                            {
                                error = string.Format("Invalid exchange, ID {0}", request.ID);
                                Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                            }
                        }
                    }
                    else
                    {
                        error = string.Format("Execution for order does not exist, ID {0}", request.ID);
                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                    }                    
                }
                else
                {
                    error = string.Format("Invalid order ID, ID {0}", request.ID);
                    Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(error));
                }
            }            
            return error;
        }
       
        private void ProcessOrderRequest(NewOrderRequest pendingOrder)
        {
            bool convertToCommonCurrency = _exchanges.First(e => e.Name == pendingOrder.Symbol.Exchange).CommonCurrency;
            //list of current order internal executions
            List<SingleExecution> executions = new List<SingleExecution>();
            try
            {
                lock (_tradingLocker)
                {
                    //find new order user account
                    var pendingOrderUserAccount = _users.FirstOrDefault(u => u.Accounts.FirstOrDefault(a => a.Name == pendingOrder.Account) != null).Accounts.FirstOrDefault(a => a.Name == pendingOrder.Account);                    
                    if (pendingOrderUserAccount != null)
                    {
                        //find new order execution
                        var pendingOrderExecution = _executions.FirstOrDefault(e => e.OrderID == pendingOrder.ID);
                        if (pendingOrderExecution != null)
                        {
                            List<NewOrderRequest> acceptedOrders = null;
                            //find orders with opposite side in local orders collection 
                            if (pendingOrder.Side == Side.Buy)
                            {
                                acceptedOrders = _activeOrders.Where(o => o.Symbol.Equals(pendingOrder.Symbol) && o.Side == Side.Sell).OrderBy(o => o.OrderType).ThenBy(o => o.LimitPrice).ThenBy(o => o.Time).ToList();
                            }
                            else
                            {
                                acceptedOrders = _activeOrders.Where(o => o.Symbol.Equals(pendingOrder.Symbol) && o.Side == Side.Buy).OrderBy(o => o.OrderType).ThenByDescending(o => o.LimitPrice).ThenBy(o => o.Time).ToList();
                            }
                            //needed quantity to fill of new order
                            decimal remainingPendingOrderQuantity = pendingOrderExecution.LeaveQuantity;
                            //pending order limit price converted to currency multiplier
                            decimal pendingOrderLimitPrice = 0;
                            if (convertToCommonCurrency)
                            {
                                pendingOrderLimitPrice = pendingOrder.LimitPrice * GetCurrencyMultiplier(pendingOrderUserAccount.Currency);
                            }
                            else
                            {
                                pendingOrderLimitPrice = pendingOrder.LimitPrice;
                            }
                            acceptedOrders.ForEach(ao =>
                            {
                                var acceptedOrderExecution = _executions.FirstOrDefault(e => e.OrderID == ao.ID);
                                if (acceptedOrderExecution != null)
                                {
                                    //find existing order user account
                                    var acceptedOrderUserAccount = _users.FirstOrDefault(u => u.Accounts.FirstOrDefault(a => a.Name == ao.Account) != null).Accounts.FirstOrDefault(a => a.Name == ao.Account);
                                    if (acceptedOrderUserAccount != null)
                                    {
                                        //needed quantity to fill of existing order
                                        decimal remainingAcceptedOrderQuantity = acceptedOrderExecution.LeaveQuantity;
                                        if (remainingAcceptedOrderQuantity > 0 && remainingPendingOrderQuantity > 0)
                                        {
                                            //accepted order limit price converted to currency multiplier
                                            decimal acceptedOrderLimitPrice = 0;
                                            if (convertToCommonCurrency)
                                            {
                                                acceptedOrderLimitPrice = ao.LimitPrice * GetCurrencyMultiplier(acceptedOrderUserAccount.Currency);
                                            }
                                            else
                                            {
                                                acceptedOrderLimitPrice = ao.LimitPrice;
                                            }
                                            //fill price
                                            decimal price = 0;
                                            //fill quantity
                                            decimal quantity = 0;
                                            if (pendingOrder.Side == Side.Buy)
                                            {
                                                //first check market and activated stop orders
                                                if (pendingOrder.OrderType == Type.Market || pendingOrder.OrderType == Type.Stop)
                                                {
                                                    //fill price defined by limit price
                                                    //fill quantity is a minimum value of remaining quantity of 2 orders
                                                    if (ao.OrderType == Type.Limit || ao.OrderType == Type.StopLimit)
                                                    {
                                                        price = acceptedOrderLimitPrice;
                                                        quantity = Math.Min(remainingPendingOrderQuantity, remainingAcceptedOrderQuantity);
                                                    }
                                                }
                                                //second check limit and activated stop limit orders
                                                else if (pendingOrder.OrderType == Type.Limit || pendingOrder.OrderType == Type.StopLimit)
                                                {
                                                    //fill price defined by limit price
                                                    //fill quantity is a minimum value of remaining quantity of 2 orders
                                                    if (ao.OrderType == Type.Market || ao.OrderType == Type.Stop)
                                                    {
                                                        price = pendingOrderLimitPrice;
                                                        quantity = Math.Min(remainingPendingOrderQuantity, remainingAcceptedOrderQuantity);
                                                    }
                                                    //fill price depends on both orders limit by limit price
                                                    //fill quantity is a minimum value of remaining quantity of 2 orders
                                                    else if ((ao.OrderType == Type.Limit || ao.OrderType == Type.StopLimit) && (acceptedOrderLimitPrice <= pendingOrderLimitPrice))
                                                    {
                                                        price = acceptedOrderLimitPrice;
                                                        quantity = Math.Min(remainingPendingOrderQuantity, remainingAcceptedOrderQuantity);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                //first check market and activated stop orders
                                                if (pendingOrder.OrderType == Type.Market || pendingOrder.OrderType == Type.Stop)
                                                {
                                                    //fill price defined by limit price
                                                    //fill quantity is a minimum value of remaining quantity of 2 orders
                                                    if (ao.OrderType == Type.Limit || ao.OrderType == Type.StopLimit)
                                                    {
                                                        price = acceptedOrderLimitPrice;
                                                        quantity = Math.Min(remainingPendingOrderQuantity, remainingAcceptedOrderQuantity);
                                                    }
                                                }
                                                //second check limit and activated stop limit orders
                                                else if (pendingOrder.OrderType == Type.Limit || pendingOrder.OrderType == Type.StopLimit)
                                                {
                                                    //fill price defined by limit price
                                                    //fill quantity is a minimum value of remaining quantity of 2 orders
                                                    if (ao.OrderType == Type.Market || ao.OrderType == Type.Stop)
                                                    {
                                                        price = pendingOrderLimitPrice;
                                                        quantity = Math.Min(remainingPendingOrderQuantity, remainingAcceptedOrderQuantity);
                                                    }
                                                    //fill price depends on both orders limit by limit price
                                                    //fill quantity is a minimum value of remaining quantity of 2 orders
                                                    else if ((ao.OrderType == Type.Limit || ao.OrderType == Type.StopLimit) && (acceptedOrderLimitPrice >= pendingOrderLimitPrice))
                                                    {
                                                        price = acceptedOrderLimitPrice;
                                                        quantity = Math.Min(remainingPendingOrderQuantity, remainingAcceptedOrderQuantity);
                                                    }
                                                }
                                            }
                                            if (quantity > 0)
                                            {
                                                decimal pendingOrderQuantity = quantity;
                                                if (pendingOrder.Side == Side.Buy)
                                                {
                                                    //check user balance for buy order converted to user account currency
                                                    if (convertToCommonCurrency)
                                                    {
                                                        pendingOrderQuantity = Math.Min((decimal)(pendingOrderUserAccount.Balance / (price / GetCurrencyMultiplier(pendingOrderUserAccount.Currency))), pendingOrderQuantity);
                                                    }
                                                    else
                                                    {
                                                        pendingOrderQuantity = Math.Min((decimal)(pendingOrderUserAccount.Balance / (price * (GetCurrencyMultiplier(pendingOrder.Symbol.Currency) / GetCurrencyMultiplier(pendingOrderUserAccount.Currency)))), pendingOrderQuantity);
                                                    }
                                                }
                                                decimal acceptedOrderQuantity = quantity;
                                                if (ao.Side == Side.Buy)
                                                {
                                                    //check user balance for buy order  converted to user account currency
                                                    if (convertToCommonCurrency)
                                                    {
                                                        acceptedOrderQuantity = Math.Min((decimal)(acceptedOrderUserAccount.Balance / (price / GetCurrencyMultiplier(acceptedOrderUserAccount.Currency))), acceptedOrderQuantity);
                                                    }
                                                    else
                                                    {
                                                        acceptedOrderQuantity = Math.Min((decimal)(acceptedOrderUserAccount.Balance / (price * (GetCurrencyMultiplier(pendingOrder.Symbol.Currency) / GetCurrencyMultiplier(acceptedOrderUserAccount.Currency)))), acceptedOrderQuantity);
                                                    }
                                                }
                                                //check filled quantity for AON order
                                                if ((pendingOrderQuantity > 0) && (acceptedOrderQuantity > 0) && ((ao.TimeInForce != TimeInForce.AON) || (ao.TimeInForce == TimeInForce.AON && quantity == ao.Quantity)))
                                                {
                                                    executions.Add(new SingleExecution() { NewOrderID = pendingOrder.ID, ExistingOrderID = ao.ID, Price = price, Quantity = Math.Min(pendingOrderQuantity, acceptedOrderQuantity) });
                                                    remainingPendingOrderQuantity -= Math.Min(pendingOrderQuantity, acceptedOrderQuantity);
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Account for order {0} does not exist", ao.ID)));
                                    }
                                }
                                else
                                {
                                    Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Execution for order {0} does not exist", ao.ID)));
                                }
                            });
                            //check fill quantity for FOK order
                            if (pendingOrder.TimeInForce == TimeInForce.FOK && executions.Sum(e => e.Quantity) < pendingOrder.Quantity)
                            {
                                CancelOrder(new CancelOrderRequest() { ID = pendingOrder.ID }, false);
                            }
                            else if ((pendingOrder.TimeInForce != TimeInForce.AON) || (pendingOrder.TimeInForce == TimeInForce.AON && executions.Sum(e => e.Quantity) == pendingOrder.Quantity))
                            {
                                DateTime matchTime = DateTime.UtcNow;
                                var lastQuote = GetLastTick(pendingOrder.Symbol);
                                //update bid/ask values and send tick
                                if (pendingOrder.OrderType == Type.Limit)
                                {
                                    if (pendingOrder.Side == Side.Buy)
                                    {
                                        lastQuote.Ask = pendingOrderLimitPrice;
                                        lastQuote.AskSize = pendingOrder.Quantity;
                                    }
                                    else
                                    {
                                        lastQuote.Bid = pendingOrderLimitPrice;
                                        lastQuote.BidSize = pendingOrder.Quantity;
                                    }
                                    lastQuote.Time = matchTime;
                                    BrodcastTick(lastQuote, false);
                                }                                
                                List<NewOrderRequest> activatedOrders = new List<NewOrderRequest>();
                                executions.ForEach(e =>
                                {
                                    //find existing order user account
                                    var acceptedOrderUserAccount = _activeOrders.Where(o => o.ID == e.ExistingOrderID).Select(o => _users.FirstOrDefault(u => u.Accounts.FirstOrDefault(a => a.Name == o.Account) != null).Accounts.FirstOrDefault(a => a.Name == o.Account)).FirstOrDefault();
                                    if (acceptedOrderUserAccount != null)
                                    {
                                        //update new order execution in local collection
                                        //update new order execution in local collection
                                        pendingOrderExecution.Time = matchTime;
                                        if (convertToCommonCurrency)
                                        {
                                            pendingOrderExecution.LastPrice = e.Price / GetCurrencyMultiplier(pendingOrderUserAccount.Currency);
                                            pendingOrderExecution.AverrageFillPrice = (pendingOrderExecution.AverrageFillPrice * pendingOrderExecution.FilledQuantity + (e.Price / GetCurrencyMultiplier(pendingOrderUserAccount.Currency)) * e.Quantity) / (pendingOrderExecution.FilledQuantity + e.Quantity);
                                        }
                                        else
                                        {
                                            pendingOrderExecution.LastPrice = e.Price;
                                            pendingOrderExecution.AverrageFillPrice = (pendingOrderExecution.AverrageFillPrice * pendingOrderExecution.FilledQuantity + e.Price * e.Quantity) / (pendingOrderExecution.FilledQuantity + e.Quantity);
                                        }
                                        pendingOrderExecution.LastQuantity = e.Quantity;
                                        pendingOrderExecution.FilledQuantity += e.Quantity;
                                        pendingOrderExecution.LeaveQuantity -= e.Quantity;
                                        pendingOrderExecution.Status = pendingOrderExecution.LeaveQuantity == 0 ? Status.Filled : Status.PartialFilled;
                                        //update existing order execution in local collection
                                        var acceptedOrderExecution = _executions.First(execution => execution.OrderID == e.ExistingOrderID);
                                        acceptedOrderExecution.Time = matchTime;
                                        if (convertToCommonCurrency)
                                        {
                                            acceptedOrderExecution.LastPrice = e.Price / GetCurrencyMultiplier(acceptedOrderUserAccount.Currency);
                                            acceptedOrderExecution.AverrageFillPrice = (acceptedOrderExecution.AverrageFillPrice * acceptedOrderExecution.FilledQuantity + (e.Price / GetCurrencyMultiplier(acceptedOrderUserAccount.Currency)) * e.Quantity) / (acceptedOrderExecution.FilledQuantity + e.Quantity);
                                        }
                                        else
                                        {
                                            acceptedOrderExecution.LastPrice = e.Price;
                                            acceptedOrderExecution.AverrageFillPrice = (acceptedOrderExecution.AverrageFillPrice * acceptedOrderExecution.FilledQuantity + e.Price  * e.Quantity) / (acceptedOrderExecution.FilledQuantity + e.Quantity);
                                        }
                                        acceptedOrderExecution.LastQuantity = e.Quantity;
                                        acceptedOrderExecution.FilledQuantity += e.Quantity;
                                        acceptedOrderExecution.LeaveQuantity -= e.Quantity;
                                        acceptedOrderExecution.Status = acceptedOrderExecution.LeaveQuantity == 0 ? Status.Filled : Status.PartialFilled;
                                        //update new and existing order account balance in local collection
                                        if (convertToCommonCurrency)
                                        {
                                            pendingOrderUserAccount.Balance += (e.Quantity * e.Price / GetCurrencyMultiplier(pendingOrderUserAccount.Currency) * (pendingOrder.Side == Side.Buy ? -1 : 1));
                                            acceptedOrderUserAccount.Balance += (e.Quantity * e.Price / GetCurrencyMultiplier(acceptedOrderUserAccount.Currency) * (pendingOrder.Side == Side.Buy ? 1 : -1));
                                        }
                                        else
                                        {
                                            pendingOrderUserAccount.Balance += (e.Quantity * e.Price * (GetCurrencyMultiplier(pendingOrder.Symbol.Currency) / GetCurrencyMultiplier(pendingOrderUserAccount.Currency)) * (pendingOrder.Side == Side.Buy ? -1 : 1));
                                            acceptedOrderUserAccount.Balance += (e.Quantity * e.Price * (GetCurrencyMultiplier(pendingOrder.Symbol.Currency) / GetCurrencyMultiplier(acceptedOrderUserAccount.Currency)) * (pendingOrder.Side == Side.Buy ? 1 : -1));
                                        }
                                        //update last price/quantity values and send tick
                                        lastQuote = GetLastTick(pendingOrder.Symbol);
                                        lastQuote.Time = matchTime;
                                        lastQuote.Price = e.Price;
                                        lastQuote.Volume = e.Quantity;
                                        BrodcastTick(new Tick(pendingOrder.Symbol, lastQuote.Time, lastQuote.Price, lastQuote.Volume, lastQuote.Bid, lastQuote.BidSize, lastQuote.Ask, lastQuote.AskSize), true);
                                        bool savedChanges = false;
                                        //save executions and accounts changes in DB in single transaction
                                        SqlConnection connection = new SqlConnection(_connectionString);
                                        SqlTransaction transaction = null;
                                        try
                                        {
                                            connection.Open();
                                            //update new order execution in DB
                                            transaction = connection.BeginTransaction();
                                            SqlCommand command = connection.CreateCommand();
                                            command.Transaction = transaction;
                                            command.CommandText = "UPDATE [Executions] SET [Time] = @Time, [LastPrice] = @LastPrice, [LastQuantity] = @LastQuantity, [FilledQuantity] = @FilledQuantity, [AverrageFillPrice] = @AverrageFillPrice, [LeaveQuantity] = @LeaveQuantity, [Status] = @Status WHERE [OrderID] = @OrderID";
                                            command.Parameters.AddWithValue("OrderID", pendingOrderExecution.OrderID);
                                            command.Parameters.AddWithValue("Time", pendingOrderExecution.Time);
                                            command.Parameters.AddWithValue("LastPrice", pendingOrderExecution.LastPrice);
                                            command.Parameters.AddWithValue("LastQuantity", pendingOrderExecution.LastQuantity);
                                            command.Parameters.AddWithValue("FilledQuantity", pendingOrderExecution.FilledQuantity);
                                            command.Parameters.AddWithValue("AverrageFillPrice", pendingOrderExecution.AverrageFillPrice);
                                            command.Parameters.AddWithValue("LeaveQuantity", pendingOrderExecution.LeaveQuantity);
                                            command.Parameters.AddWithValue("Status", (byte)pendingOrderExecution.Status);
                                            command.ExecuteNonQuery();
                                            //update existing order execution in DB
                                            command = connection.CreateCommand();
                                            command.Transaction = transaction;
                                            command.CommandText = "UPDATE [Executions] SET [Time] = @Time, [LastPrice] = @LastPrice, [LastQuantity] = @LastQuantity, [FilledQuantity] = @FilledQuantity, [AverrageFillPrice] = @AverrageFillPrice, [LeaveQuantity] = @LeaveQuantity, [Status] = @Status WHERE [OrderID] = @OrderID";
                                            command.Parameters.AddWithValue("OrderID", acceptedOrderExecution.OrderID);
                                            command.Parameters.AddWithValue("Time", acceptedOrderExecution.Time);
                                            command.Parameters.AddWithValue("LastPrice", acceptedOrderExecution.LastPrice);
                                            command.Parameters.AddWithValue("LastQuantity", acceptedOrderExecution.LastQuantity);
                                            command.Parameters.AddWithValue("FilledQuantity", acceptedOrderExecution.FilledQuantity);
                                            command.Parameters.AddWithValue("AverrageFillPrice", acceptedOrderExecution.AverrageFillPrice);
                                            command.Parameters.AddWithValue("LeaveQuantity", acceptedOrderExecution.LeaveQuantity);
                                            command.Parameters.AddWithValue("Status", (byte)acceptedOrderExecution.Status);
                                            command.ExecuteNonQuery();
                                            //update new order account balance in DB
                                            command = connection.CreateCommand();
                                            command.Transaction = transaction;
                                            command.CommandText = "UPDATE [Accounts] SET [Balance] = @Balance WHERE [Name] = @Name";
                                            command.Parameters.AddWithValue("Name", pendingOrderUserAccount.Name);
                                            command.Parameters.AddWithValue("Balance", pendingOrderUserAccount.Balance);
                                            command.ExecuteNonQuery();
                                            //update existing order account balance in DB
                                            command = connection.CreateCommand();
                                            command.Transaction = transaction;
                                            command.CommandText = "UPDATE [Accounts] SET [Balance] = @Balance WHERE [Name] = @Name";
                                            command.Parameters.AddWithValue("Name", acceptedOrderUserAccount.Name);
                                            command.Parameters.AddWithValue("Balance", acceptedOrderUserAccount.Balance);
                                            command.ExecuteNonQuery();
                                            transaction.Commit();
                                            savedChanges = true;
                                        }
                                        catch (Exception ex)
                                        {
                                            if (transaction != null)
                                            {
                                                transaction.Rollback();
                                            }
                                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), ex);
                                        }
                                        if (connection.State == System.Data.ConnectionState.Open)
                                        {
                                            connection.Close();
                                        }
                                        if (savedChanges)
                                        {
                                            //send executions and accounts modification notification
                                            FireExecution(acceptedOrderUserAccount.Name, acceptedOrderExecution);
                                            FireExecution(pendingOrderUserAccount.Name, pendingOrderExecution);
                                            FireAccountInfo(pendingOrderUserAccount);
                                            FireAccountInfo(acceptedOrderUserAccount);
                                            Log.WriteMatchExecutions(pendingOrder, _activeOrders.First(ao=>ao.ID == acceptedOrderExecution.OrderID),pendingOrderExecution, acceptedOrderExecution);
                                        }
                                    }
                                });
                                //update IOC order execution
                                if (pendingOrder.TimeInForce == TimeInForce.IOC && pendingOrderExecution.Status != Status.Filled)
                                {
                                    pendingOrderExecution.CancelledQuantity = pendingOrderExecution.LeaveQuantity;
                                    pendingOrderExecution.LeaveQuantity = 0;
                                    pendingOrderExecution.Status = Status.Done;
                                    SqlConnection connection = new SqlConnection(_connectionString);
                                    try
                                    {
                                        connection.Open();
                                        SqlCommand command = connection.CreateCommand();
                                        command.CommandText = "UPDATE [Executions] SET [Time] = @Time, [Status] = @Status, [LeaveQuantity] = 0, [CancelledQuantity] = @CancelledQuantity WHERE [OrderID] = @OrderID";
                                        command.Parameters.AddWithValue("OrderID", pendingOrderExecution.OrderID);
                                        command.Parameters.AddWithValue("Status", (byte)pendingOrderExecution.Status);
                                        command.Parameters.AddWithValue("Time", pendingOrderExecution.Time);
                                        command.Parameters.AddWithValue("LeaveQuantity", pendingOrderExecution.LeaveQuantity);
                                        command.Parameters.AddWithValue("CancelledQuantity", pendingOrderExecution.CancelledQuantity);
                                        command.ExecuteNonQuery();
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), ex);
                                    }
                                    if (connection.State == System.Data.ConnectionState.Open)
                                    {
                                        connection.Close();
                                    }
                                    //send executions modification notification
                                    FireExecution(pendingOrderUserAccount.Name, pendingOrderExecution);
                                }
                                //remove filled orders/executions from local collection
                                _executions.FindAll(e => e.Status == Status.Filled).ForEach(e => _activeOrders.RemoveAll(o => o.ID == e.OrderID));
                                _executions.RemoveAll(e => e.Status == Status.Filled);
                                executions.ForEach(e =>
                                {
                                    //activate stop orders by curent filled prices
                                    _stopOorders.FindAll(o => o.Symbol.Equals(pendingOrder.Symbol)).OrderBy(o => o.Time).ToList().ForEach(o =>
                                    {
                                        DateTime activationTime = DateTime.UtcNow;
                                        //save stop orders activation time in database
                                        Func<string, bool> updateStatus = (orderID) =>
                                        {
                                            bool saved = false;
                                            var stopOrderExecution = _executions.FirstOrDefault(ex => ex.OrderID == orderID);
                                            if (stopOrderExecution != null)
                                            {
                                                SqlConnection connection = new SqlConnection(_connectionString);
                                                SqlTransaction transaction = null;
                                                try
                                                {
                                                    connection.Open();
                                                    transaction = connection.BeginTransaction();
                                                    SqlCommand command = connection.CreateCommand();
                                                    command.Transaction = transaction;
                                                    command.CommandText = "UPDATE [Orders] SET [ActivationTime] = @ActivationTime WHERE [ID] = @ID";
                                                    command.Parameters.AddWithValue("ID", orderID);
                                                    command.Parameters.AddWithValue("ActivationTime", activationTime);
                                                    command.ExecuteNonQuery();

                                                    command = connection.CreateCommand();
                                                    command.Transaction = transaction;
                                                    command.CommandText = "UPDATE [Executions] SET [Status] = @Status WHERE [OrderID] = @OrderID";
                                                    command.Parameters.AddWithValue("OrderID", orderID);
                                                    command.Parameters.AddWithValue("Status", (byte)Status.Activated);
                                                    command.ExecuteNonQuery();
                                                    transaction.Commit();
                                                    saved = true;
                                                }
                                                catch (Exception ex)
                                                {
                                                    if (transaction != null)
                                                    {
                                                        transaction.Rollback();
                                                    }
                                                    Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), ex);
                                                }
                                                if (connection.State == System.Data.ConnectionState.Open)
                                                {
                                                    connection.Close();
                                                }
                                                stopOrderExecution.Status = Status.Activated;
                                            }
                                            return saved;
                                        };
                                        var stopOrderUserAccount = _users.FirstOrDefault(u => u.Accounts.FirstOrDefault(a => a.Name == o.Account) != null).Accounts.FirstOrDefault(a => a.Name == o.Account);
                                        if (stopOrderUserAccount != null)
                                        {
                                            //calculate execution price in stop order currency
                                            decimal executionPrice = 0;
                                            if (convertToCommonCurrency)
                                            {
                                                executionPrice = e.Price / GetCurrencyMultiplier(stopOrderUserAccount.Currency);
                                            }
                                            else
                                            {
                                                executionPrice = e.Price * GetCurrencyMultiplier(stopOrderUserAccount.Currency);
                                            }
                                            //activate stop order based on last prices
                                            if (o.OrderType == Type.Stop)
                                            {
                                                if ((o.Side == Side.Buy && executionPrice >= o.StopPrice) || (o.Side == Side.Sell && executionPrice <= o.StopPrice))
                                                {
                                                    if (o.ActivationTime == DateTime.MinValue && updateStatus(o.ID))
                                                    {
                                                        o.ActivationTime = activationTime;
                                                    }
                                                }
                                            }
                                            //activate stop limit order based on last prices
                                            //update bid/ask values and send tick
                                            else if (o.OrderType == Type.StopLimit)
                                            {
                                                lastQuote = GetLastTick(pendingOrder.Symbol);
                                                if (o.Side == Side.Buy && executionPrice >= o.StopPrice)
                                                {
                                                    if (o.ActivationTime == DateTime.MinValue && updateStatus(o.ID))
                                                    {
                                                        o.ActivationTime = activationTime;
                                                        lastQuote.Ask = pendingOrder.LimitPrice;
                                                        lastQuote.AskSize = pendingOrder.Quantity;
                                                        lastQuote.Time = matchTime;
                                                        BrodcastTick(lastQuote, false);
                                                    }
                                                }
                                                else if (o.Side == Side.Sell && executionPrice <= o.StopPrice)
                                                {
                                                    if (o.ActivationTime == DateTime.MinValue && updateStatus(o.ID))
                                                    {
                                                        o.ActivationTime = activationTime;
                                                        lastQuote.Bid = pendingOrder.LimitPrice;
                                                        lastQuote.BidSize = pendingOrder.Quantity;
                                                        lastQuote.Time = matchTime;
                                                        BrodcastTick(lastQuote, false);
                                                    }
                                                }
                                            }
                                            if (o.ActivationTime == activationTime)
                                            {
                                                //move activated stop orders from stop orders collection to market/limit orders collection
                                                _activeOrders.Add(o);
                                                _stopOorders.Remove(o);
                                                activatedOrders.Add(o);
                                            }
                                        }
                                    });
                                });
                                lock (_activatedStopOrders)
                                {
                                    activatedOrders.ForEach(o => 
                                    {
                                        var stopOrderExecution = _executions.FirstOrDefault(e => e.OrderID == o.ID);
                                        if (stopOrderExecution != null)
                                        {
                                            FireExecution(o.Account, stopOrderExecution);
                                        }
                                        _activatedStopOrders.Enqueue(o);
                                    });
                                }
                                if (activatedOrders.Count > 0)
                                {
                                    //set system event to process activated stop orders
                                    _processStopOrdersEvent.Set();
                                }
                            }
                        }
                        else
                        {
                            Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Execution for order {0} does not exist", pendingOrder.ID)));
                        }
                    }
                    else
                    {
                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Account for order {0} does not exist", pendingOrder.ID)));
                    }                    
                }
            }
            catch (Exception ex)
            {
                Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), ex);
            }
        }
                
        private void ProcessStopOrdersHandler()
        {
            while (_started)
            {
                _processStopOrdersEvent.WaitOne();
                _processStopOrdersEvent.Reset();
                while (_activatedStopOrders.Count > 0)
                {
                    NewOrderRequest request = null;
                    lock (_activatedStopOrders)
                    {
                        request = _activatedStopOrders.Dequeue();
                    }
                    if (request != null)
                    {
                        ProcessOrderRequest(request);
                    }
                }
            }
        }
               
        private void ProcessExchangeSession()
        {
            while (true)
            {
                DateTime utcNow = DateTime.UtcNow;
                List<string> orderIDs;
                lock (_tradingLocker)
                {   
                    orderIDs = _activeOrders.Where(o => (o.TimeInForce == TimeInForce.GTD || o.TimeInForce == TimeInForce.DAY || o.TimeInForce == TimeInForce.AON) && o.ExpirationDate <= utcNow).Select(o => o.ID).ToList();
                }
                //cancel expiried market and limit orders
                orderIDs.ForEach(id => CancelOrder(new CancelOrderRequest() { ID = id }, false));
                lock (_tradingLocker)
                {
                    orderIDs = _stopOorders.Where(o => (o.TimeInForce == TimeInForce.GTD || o.TimeInForce == TimeInForce.DAY || o.TimeInForce == TimeInForce.AON) && o.ExpirationDate <= utcNow).Select(o => o.ID).ToList();
                }
                //cancel expiried stop orders
                orderIDs.ForEach(id => CancelOrder(new CancelOrderRequest() { ID = id }, false));
                lock (_exchanges)
                {
                    _exchanges.ForEach(exchange =>
                    {
                        if (utcNow > exchange.EndDate)
                        {
                            //update exchange current day trading hours
                            exchange.UpdateDateTime();
                            _cache.ProcessExchangeSession(exchange.Name);
                        }
                    });
                }                
                Thread.Sleep(1000);
            }
        }
        
        //fires order update notification
        private void FireExecution(string account, Execution execution)
        {
            User user = _users.FirstOrDefault(u => u.Accounts.FirstOrDefault(a => a.Name == account) != null);
            if (user != null && OnExecution != null)
            {
                OnExecution(user.UserName, execution);
            }
            else
            {
                Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception("Invalid user"));
            }
        }

        //fires account update notification
        private void FireAccountInfo(Account account)
        {
            User user = _users.FirstOrDefault(u => u.Accounts.FirstOrDefault(a => a.Name == account.Name) != null);
            if (user != null && OnAccountInfo != null)
            {
                OnAccountInfo(user.UserName, account);
            }
            else
            {
                Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception("Invalid user"));
            }
        }

        //add tick to cache and fires tick update notification
        private void BrodcastTick(Tick tick, bool save)
        {
            _cache.AppendTick(tick, save);
            if (OnTick != null)
            {
                var baseMultiplier = GetCurrencyMultiplier(tick.Symbol.Currency);
                _currencies.ForEach(c =>
                {
                    if (baseMultiplier != 0)
                    {
                        var multiplier = baseMultiplier / c.Multiplier;
                        OnTick(new Tick() { Symbol = tick.Symbol, Currency = c.Name, Time = tick.Time, Price = tick.Price * multiplier, Bid = tick.Bid * multiplier, BidSize = tick.BidSize, Ask = tick.Ask * multiplier, AskSize = tick.AskSize, Volume = tick.Volume });
                    }
                    else
                    {
                        Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), new Exception(string.Format("Currency {0} does not exist", tick.Symbol.Currency)));
                    }
                });
                OnTick(tick);
            }
        }
    }   
}