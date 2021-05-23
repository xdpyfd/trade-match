
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Reflection;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Config;
using SuperWebSocket;
using System.Data;
using System.Threading;
using System.IO;
using System.Runtime.Serialization.Json;

namespace NDAXCore
{
    //messages exchange class
    public class Server
    {
        private class AdminSession
        {
            public string UserName;
            public WebSocketSession Session;
        }

        private class SusbcibedItem
        {
            public Symbol Symbol;
            public string Currency;
        }

        private class UserSession 
        {
            public string UserName;
            public WebSocketSession Session;
            public List<SusbcibedItem> Subscribers;
        }

        private string _connectionString = ConfigurationManager.ConnectionStrings["NDAXCore"].ConnectionString;

        private WebSocketServer _adminServer;
        private List<AdminSession> _admins = new List<AdminSession>();

        private WebSocketServer _brokerServer;
        private DataFeed _dataFeed = null;
        private List<UserSession> _connectedUsers = new List<UserSession>();
        private bool _started;

        //start web socket listeners for admin and user sessions
        public string Start(string adminIP, int adminPort, string brokerIP, int brokerPort)
        {
            _started = false;
            string result = "";
            _adminServer = new WebSocketServer();
            lock (_adminServer)
            {
                //start admin sessions listener
                if (_adminServer.Setup(new ServerConfig() { Ip = adminIP, Port = adminPort, MaxRequestLength = short.MaxValue, MaxConnectionNumber = short.MaxValue }))
                {
                    if (!_adminServer.Start())
                    {
                        result = "Failed to setup admin listener";
                    }
                }
                else
                {
                    result = "Failed to start admin listener";
                }
            }
            if (string.IsNullOrEmpty(result))
            {
                _brokerServer = new WebSocketServer();
                lock (_brokerServer)
                {
                    //start user sessions listener
                    if (_brokerServer.Setup(new ServerConfig() { Ip = brokerIP, Port = brokerPort, MaxRequestLength = short.MaxValue, MaxConnectionNumber = short.MaxValue }))
                    {
                        if (!_brokerServer.Start())
                        {
                            result = "Failed to setup user listener";
                        }
                    }
                    else
                    {
                        result = "Failed to start user listener";
                    }
                }
                if (string.IsNullOrEmpty(result))
                {
                    _dataFeed = new DataFeed();
                    result = _dataFeed.Start();
                    if (!string.IsNullOrEmpty(result))
                    {
                        _dataFeed.Stop();
                        lock (_adminServer)
                        {
                            _adminServer.Stop();
                        }
                        lock (_brokerServer)
                        {
                            _brokerServer.Stop();
                        }
                    }
                    else
                    {
                        //subscribe to events
                        _dataFeed.OnTick += DataFeed_OnTick;
                        _dataFeed.OnExecution += DataFeed_OnExecution;
                        _dataFeed.OnAccountInfo += DataFeed_OnAccountInfo;
                        _dataFeed.OnMessageResponse += DataFeed_OnMessageResponse;
                        _adminServer.NewMessageReceived += AdminServerOnMessageReceived;
                        _adminServer.SessionClosed += AdminServerOnSessionClosed;
                        _brokerServer.NewMessageReceived += BrokerServerOnMessageReceived;
                        _brokerServer.SessionClosed += BrokerServerOnSessionClosed;
                        _started = true;
                    }
                }
                else
                {
                    lock (_adminServer)
                    {
                        _adminServer.Stop();
                    }
                    lock (_brokerServer)
                    {
                        _brokerServer.Stop();
                    }
                }
            }
            else
            {
                lock (_adminServer)
                {
                    _adminServer.Stop();
                    _adminServer.Dispose();
                }
            }
            return result;
        }

        public void Stop()
        {
            _started = false;

            _dataFeed.OnTick -= DataFeed_OnTick;
            _dataFeed.OnExecution -= DataFeed_OnExecution;
            _dataFeed.OnAccountInfo -= DataFeed_OnAccountInfo;
            _dataFeed.Stop();
            _dataFeed = null;
            //stop admin sessions listener
            lock (_adminServer)
            {
                _adminServer.NewMessageReceived -= AdminServerOnMessageReceived;
                _adminServer.SessionClosed -= AdminServerOnSessionClosed;
                _adminServer.Stop();
                _adminServer = null;
            }
            //stop user sessions listener
            lock (_brokerServer)
            {
                _brokerServer.NewMessageReceived -= BrokerServerOnMessageReceived;
                _brokerServer.SessionClosed -= BrokerServerOnSessionClosed;
                _brokerServer.Stop();
                _brokerServer = null;
            }
        }

        //send tick for all subscribed users
        private void DataFeed_OnTick(Tick tick)
        {
            lock (_connectedUsers)
            {
                _connectedUsers.FindAll(u => u.Subscribers.Count(s => s.Symbol.Equals(tick.Symbol) && s.Currency == tick.Currency) != 0).ForEach(u => { u.Session.Send(Serialize(tick)); });
            }
        }

        //send oder update notification for user
        private void DataFeed_OnExecution(string userName, Execution execution)
        {
            lock (_connectedUsers)
            {
                _connectedUsers.FindAll(u => u.UserName == userName).ForEach(u => { u.Session.Send(Serialize(execution)); });
            }
        }

        //send account update notification for user
        private void DataFeed_OnAccountInfo(string userName, Account account)
        {
            lock (_connectedUsers)
            {
                _connectedUsers.FindAll(u => u.UserName == userName).ForEach(u => { u.Session.Send(Serialize(account)); });
            }
        }

        private void DataFeed_OnMessageResponse(string userName, string messageID, string error)
        {
            lock (_connectedUsers)
            {
                _connectedUsers.FindAll(u => u.UserName == userName).ForEach(u => { u.Session.Send(Serialize(new MessageResponse() { MessageID = messageID, Result = error })); });
            }
        }

        //admin session closed notification
        private void AdminServerOnSessionClosed(WebSocketSession session, CloseReason r)
        {
            lock (_admins)
            {
                _admins.RemoveAll(a => a.Session == session);
            }
        }

        //handles exchange admin messages 
        private void AdminServerOnMessageReceived(WebSocketSession session, string message)
        {
            var baseMessage = Deserialize<Message>(message);
            if (baseMessage != null)
            {
                if (baseMessage.MessageType == "LoginRequest")
                {
                    var request = Deserialize<LoginRequest>(message);
                    if (request != null)
                    {
                        //response contains login result, list if users, exchanges
                        AdminLoginResponse result = new AdminLoginResponse() { MessageID = request.MessageID, AdminLoginResult = LoginResult.InternalError, Users = new List<User>(), Currencies = new List<Currency>(), Exchanges = new List<ExchangeSettings>() };
                        lock (_admins)
                        {
                            //if admin is already logged in then disconnect previous session and new session
                            var admin = _admins.FirstOrDefault(a => a.UserName == request.UserName);
                            if (admin != null)
                            {
                                _admins.Remove(admin);
                                admin.Session.Send(Serialize(new ServerInfoMessage() { Info = "Another user logged in with the same credentials", Exit = true }));
                                result.AdminLoginResult = LoginResult.AlreadyLogged;
                            }
                            else
                            {
                                SqlConnection connection = new SqlConnection(_connectionString);
                                try
                                {
                                    //check credentials
                                    connection.Open();
                                    SqlCommand command = connection.CreateCommand();
                                    command.CommandText = "SELECT * FROM [Admins] WHERE [UserName] = @UserName AND [Password] = @Password";
                                    command.Parameters.AddWithValue("UserName", request.UserName);
                                    command.Parameters.AddWithValue("Password", request.Password);
                                    SqlDataReader reader = command.ExecuteReader();
                                    if (reader.HasRows)
                                    {
                                        reader.Close();
                                        //get users
                                        command = connection.CreateCommand();
                                        command.CommandText = "SELECT u.[UserName], u.[Password], a.[Name], a.[Balance], a.[Currency] FROM [Users] u, [Accounts] a WHERE u.[UserName] = a.[UserName] ORDER BY u.[UserName], a.[Name]";
                                        reader = command.ExecuteReader();
                                        if (reader.HasRows)
                                        {
                                            while (reader.Read())
                                            {
                                                string userName = reader.GetString(0);
                                                if (result.Users.FirstOrDefault(u => u.UserName == userName) == null)
                                                {
                                                    result.Users.Add(new User() { UserName = userName, Password = reader.GetString(1), Accounts = new List<Account>() });
                                                }
                                                result.Users.First(u => u.UserName == userName).Accounts.Add(new Account() { Name = reader.GetString(2), Balance = reader.GetDecimal(3), Currency = reader.GetString(4) });
                                            }
                                        }
                                        reader.Close();
                                        //get currencies
                                        command = connection.CreateCommand();
                                        command.CommandText = "SELECT * FROM [Currencies] ORDER BY [Name]";
                                        reader = command.ExecuteReader();
                                        if (reader.HasRows)
                                        {
                                            while (reader.Read())
                                            {
                                                result.Currencies.Add(new Currency() { Name = reader.GetString(0), Multiplier = reader.GetDecimal(1) });
                                            }
                                        }
                                        reader.Close();
                                        //get exchanges
                                        command = connection.CreateCommand();
                                        command.CommandText = "SELECT e.[Name], e.[StartTime], e.[EndTime], e.[CommonCurrency], s.[Name], s.[Exchange], s.[Currency] FROM [Exchanges] e, [Symbols] s WHERE e.[Name] = s.[Exchange] ORDER BY e.[Name]";
                                        reader = command.ExecuteReader();
                                        if (reader.HasRows)
                                        {
                                            while (reader.Read())
                                            {
                                                string exchangeName = reader.GetString(0);
                                                if (result.Exchanges.FirstOrDefault(e => e.Name == exchangeName) == null)
                                                {
                                                    result.Exchanges.Add(new ExchangeSettings() { Name = reader.GetString(0), StartTime = reader.GetTimeSpan(1), EndTime = reader.GetTimeSpan(2), CommonCurrency = reader.GetBoolean(3), Symbols = new List<Symbol>() });
                                                }
                                                result.Exchanges.First(e => e.Name == exchangeName).Symbols.Add(new Symbol() { Name = reader.GetString(4), Exchange = reader.GetString(5), Currency = reader.GetString(6) });
                                            }
                                        }
                                        reader.Close();
                                        result.AdminLoginResult = LoginResult.OK;
                                        _admins.Add(new AdminSession() { UserName = request.UserName, Session = session });
                                    }
                                    else
                                    {
                                        reader.Close();
                                        result.AdminLoginResult = LoginResult.InvalidCredentials;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    result.AdminLoginResult = LoginResult.InternalError;
                                    result.Exchanges.Clear();
                                    result.Users.Clear();
                                    Log.WriteApplicationException(MethodBase.GetCurrentMethod().DeclaringType.Name, MethodBase.GetCurrentMethod().Name, new System.Diagnostics.StackFrame(0, true).GetFileLineNumber(), ex);
                                }
                                if (connection.State == System.Data.ConnectionState.Open)
                                {
                                    connection.Close();
                                }
                            }
                        }
                        session.Send(Serialize(result));
                    }
                }
                else if (baseMessage.MessageType == "AddUserRequest")
                {
                    var request = Deserialize<AddUserRequest>(message);
                    if (request != null)
                    {
                        string result = null;
                        if (request.User.Accounts.Count > 0)
                        {
                            //add record(s) to users and accounts tables in single transaction
                            SqlConnection connection = new SqlConnection(_connectionString);
                            SqlTransaction transaction = null;
                            try
                            {
                                connection.Open();
                                transaction = connection.BeginTransaction();
                                SqlCommand command = connection.CreateCommand();
                                command.Transaction = transaction;
                                command.CommandText = "INSERT INTO [Users] VALUES (@UserName, @Password)";
                                command.Parameters.AddWithValue("UserName", request.User.UserName);
                                command.Parameters.AddWithValue("Password", request.User.Password);
                                command.ExecuteNonQuery();
                                foreach (var account in request.User.Accounts)
                                {
                                    command = connection.CreateCommand();
                                    command.Transaction = transaction;
                                    command.CommandText = "INSERT [Accounts] VALUES (@UserName, @Name, @Balance, @Currency)";
                                    command.Parameters.AddWithValue("UserName", request.User.UserName);
                                    command.Parameters.AddWithValue("Name", account.Name);
                                    command.Parameters.AddWithValue("Balance", account.Balance);
                                    command.Parameters.AddWithValue("Currency", account.Currency);
                                    command.ExecuteNonQuery();
                                }
                                transaction.Commit();
                            }
                            catch (Exception ex)
                            {
                                if (transaction != null)
                                {
                                    transaction.Rollback();
                                }
                                result = ex.Message;
                            }
                            if (connection.State == System.Data.ConnectionState.Open)
                            {
                                connection.Close();
                            }
                        }
                        else
                        {
                            result = "Invalid accounts";
                        }
                        session.Send(Serialize(new MessageResponse() { MessageID = request.MessageID, Result = result }));
                        if (string.IsNullOrEmpty(result))
                        {
                            //update users list in order matching engine 
                            _dataFeed.AddUser(request.User);
                        }
                    }
                }
                else if (baseMessage.MessageType == "EditUserRequest")
                {
                    var request = Deserialize<EditUserRequest>(message);
                    if (request != null)
                    {
                        string result = null;
                        if (request.User.Accounts.Count > 0)
                        {
                            //modify record(s) in users and accounts tables in single transaction
                            SqlConnection connection = new SqlConnection(_connectionString);
                            SqlTransaction transaction = null;
                            try
                            {
                                connection.Open();
                                transaction = connection.BeginTransaction();
                                SqlCommand command = connection.CreateCommand();
                                command.Transaction = transaction;
                                command.CommandText = "SELECT u.[UserName], a.[Name], a.[Balance] FROM [Users] u, [Accounts] a WHERE u.[UserName] = @UserName AND u.[UserName] = a.[UserName] ORDER BY u.[UserName], a.[Name]";
                                command.Parameters.AddWithValue("UserName", request.OldUserName);
                                var reader = command.ExecuteReader();
                                List<Account> oldAccounts = new List<Account>();
                                if (reader.HasRows)
                                {
                                    while (reader.Read())
                                    {
                                        oldAccounts.Add(new Account() { Name = reader.GetString(1), Balance = reader.GetDecimal(2) });
                                    }
                                }
                                reader.Close();
                                var accountsToRemove = oldAccounts.Except(request.User.Accounts, new AccountComparer()).ToList();
                                var accountsToEdit = request.User.Accounts.Intersect(oldAccounts, new AccountComparer()).ToList();
                                var accountsToAdd = request.User.Accounts.Except(oldAccounts, new AccountComparer()).ToList();
                                command = connection.CreateCommand();
                                command.Transaction = transaction;
                                command.CommandText = "UPDATE [Users] SET [UserName] = @NewUserName, [Password] = @Password WHERE [UserName] = @OldUserName";
                                command.Parameters.AddWithValue("NewUserName", request.User.UserName);
                                command.Parameters.AddWithValue("Password", request.User.Password);
                                command.Parameters.AddWithValue("OldUserName", request.OldUserName);
                                command.ExecuteNonQuery();
                                foreach (var account in accountsToRemove)
                                {
                                    command = connection.CreateCommand();
                                    command.Transaction = transaction;
                                    command.CommandText = "DELETE FROM [Accounts] WHERE [Name] = @Name";
                                    command.Parameters.AddWithValue("Name", account.Name);
                                    command.ExecuteNonQuery();
                                }
                                foreach (var account in accountsToEdit)
                                {
                                    command = connection.CreateCommand();
                                    command.Transaction = transaction;
                                    command.CommandText = "UPDATE [Accounts] SET [Balance] = @Balance, [Currency] = @Currency WHERE [Name] = @Name";
                                    command.Parameters.AddWithValue("Name", account.Name);
                                    command.Parameters.AddWithValue("Balance", account.Balance);
                                    command.Parameters.AddWithValue("Currency", account.Currency);
                                    command.ExecuteNonQuery();
                                }
                                foreach (var account in accountsToAdd)
                                {
                                    command = connection.CreateCommand();
                                    command.Transaction = transaction;
                                    command.CommandText = "INSERT INTO [Accounts] VALUES (@UserName, @Name, @Balance, @Currency)";
                                    command.Parameters.AddWithValue("UserName", request.User.UserName);
                                    command.Parameters.AddWithValue("Name", account.Name);
                                    command.Parameters.AddWithValue("Balance", account.Balance);
                                    command.Parameters.AddWithValue("Currency", account.Currency);
                                    command.ExecuteNonQuery();
                                }
                                transaction.Commit();
                            }
                            catch (Exception ex)
                            {
                                if (transaction != null)
                                {
                                    transaction.Rollback();
                                }
                                result = ex.Message;
                            }
                            if (connection.State == System.Data.ConnectionState.Open)
                            {
                                connection.Close();
                            }
                        }
                        else
                        {
                            result = "Invalid accounts";
                        }
                        session.Send(Serialize(new MessageResponse() { MessageID = request.MessageID, Result = result }));
                        if (string.IsNullOrEmpty(result))
                        {
                            //send info to updated user
                            lock (_connectedUsers)
                            {
                                var connectedUser = _connectedUsers.FirstOrDefault(u => u.UserName == request.OldUserName);
                                if (connectedUser != null)
                                {
                                    connectedUser.UserName = request.User.UserName;
                                    request.User.Accounts.ForEach(a => { connectedUser.Session.Send(Serialize(a)); });
                                    connectedUser.Session.Send(Serialize(new ServerInfoMessage() { Info = "Your account has been changed", Exit = false }));
                                }
                            }
                            //update users list in order matching engine
                            _dataFeed.EditUser(request.OldUserName, request.User);
                        }
                    }
                }
                else if (baseMessage.MessageType == "DeleteUserRequest")
                {
                    var request = Deserialize<DeleteUserRequest>(message);
                    if (request != null)
                    {
                        string result = null;
                        //delete record from users and accounts tables 
                        SqlConnection connection = new SqlConnection(_connectionString);
                        try
                        {
                            connection.Open();
                            SqlCommand command = connection.CreateCommand();
                            command.CommandText = "DELETE FROM [Users] WHERE [UserName] = @UserName";
                            command.Parameters.AddWithValue("UserName", request.UserName);
                            command.ExecuteNonQuery();
                        }
                        catch (Exception ex)
                        {
                            result = ex.Message;
                        }
                        if (connection.State == System.Data.ConnectionState.Open)
                        {
                            connection.Close();
                        }
                        session.Send(Serialize(new MessageResponse() { MessageID = request.MessageID, Result = result }));
                        if (string.IsNullOrEmpty(result))
                        {
                            //send info to deleted user
                            lock (_connectedUsers)
                            {
                                var connectedUser = _connectedUsers.FirstOrDefault(u => u.UserName == request.UserName);
                                if (connectedUser != null)
                                {
                                    connectedUser.Session.Send(Serialize(new ServerInfoMessage() { Info = "Your account has been removed", Exit = true }));
                                    _connectedUsers.Remove(connectedUser);
                                }
                            }
                            //update users list in order matching engine
                            _dataFeed.DeleteUser(request.UserName);
                        }
                    }
                }
                else if (baseMessage.MessageType == "AddExchangeRequest")
                {
                    var request = Deserialize<AddExchangeRequest>(message);
                    if (request != null)
                    {
                        ExchangeSettingsEx exchange = new ExchangeSettingsEx(request.Exchange);
                        string result = null;
                        if (exchange.StartTime < exchange.EndTime)
                        {
                            //add record(s) to exchanges and symbols tables in single transaction
                            SqlTransaction transaction = null;
                            SqlConnection connection = new SqlConnection(_connectionString);
                            try
                            {
                                connection.Open();
                                transaction = connection.BeginTransaction();
                                SqlCommand command = connection.CreateCommand();
                                command.Transaction = transaction;
                                List<string> currencies = new List<string>();
                                command.CommandText = "SELECT [Name] FROM [Currencies]";
                                var reader = command.ExecuteReader();
                                if (reader.HasRows)
                                {
                                    while (reader.Read())
                                    {
                                        currencies.Add(reader.GetString(0));
                                    }
                                }
                                reader.Close();
                                command = connection.CreateCommand();
                                command.Transaction = transaction;
                                command.CommandText = "INSERT INTO [Exchanges] VALUES (@Name, @StartTime, @EndTime, @CommonCurrency)";
                                command.Parameters.AddWithValue("Name", request.Exchange.Name);
                                command.Parameters.AddWithValue("StartTime", request.Exchange.StartTime);
                                command.Parameters.AddWithValue("EndTime", request.Exchange.EndTime);
                                command.Parameters.AddWithValue("CommonCurrency", request.Exchange.CommonCurrency);
                                command.ExecuteNonQuery();
                                DataTable table = new DataTable();
                                table.Columns.Add("ID", typeof(string));
                                table.Columns.Add("Name", typeof(string));
                                table.Columns.Add("Exchange", typeof(string));
                                table.Columns.Add("Currency", typeof(string));
                                if (request.Exchange.CommonCurrency)
                                {
                                    if (currencies.Contains("BASE"))
                                    {
                                        ExchangeSymbol symbol = new ExchangeSymbol() { ID = Guid.NewGuid().ToString(), Symbol = new Symbol() { Name = "BTC", Exchange = request.Exchange.Name, Currency = "BASE" } };
                                        table.Rows.Add(symbol.ID, symbol.Symbol.Name, symbol.Symbol.Exchange, symbol.Symbol.Currency);
                                        exchange.Symbols.Add(symbol);
                                    }
                                }
                                else
                                {
                                    request.Exchange.Symbols.ForEach(item =>
                                    {
                                        if (currencies.Contains(item.Currency))
                                        {
                                            ExchangeSymbol symbol = new ExchangeSymbol() { ID = Guid.NewGuid().ToString(), Symbol = new Symbol() { Name = item.Name, Exchange = item.Exchange, Currency = item.Currency } };
                                            table.Rows.Add(symbol.ID, symbol.Symbol.Name, symbol.Symbol.Exchange, symbol.Symbol.Currency);
                                            exchange.Symbols.Add(symbol);
                                        }
                                    });
                                }
                                if (table.Rows.Count > 0)
                                {
                                    SqlBulkCopy copy = new SqlBulkCopy(connection, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock, transaction);
                                    copy.DestinationTableName = "Symbols";
                                    copy.WriteToServer(table);
                                    transaction.Commit();
                                }
                                else
                                {
                                    result = "Invalid symbols";
                                }
                            }
                            catch (Exception ex)
                            {
                                if (transaction != null)
                                {
                                    transaction.Rollback();
                                }
                                result = ex.Message;
                            }
                            if (connection.State == System.Data.ConnectionState.Open)
                            {
                                connection.Close();
                            }
                        }
                        else
                        {
                            result = "Invalid exchange session time. Start time must be less then end time.";
                        }
                        session.Send(Serialize(new MessageResponse() { MessageID = request.MessageID, Result = result }));
                        if (string.IsNullOrEmpty(result))
                        {
                            //update exchanges in order matching engine 
                            _dataFeed.AddExchange(exchange);
                        }
                    }
                }
                else if (baseMessage.MessageType == "EditExchangeRequest")
                {
                    var request = Deserialize<EditExchangeRequest>(message);
                    if (request != null)
                    {   
                        string result = null;
                        var exchange = new ExchangeSettingsEx() { Name = request.Exchange.Name, StartTime = request.Exchange.StartTime, EndTime = request.Exchange.EndTime, CommonCurrency = request.Exchange.CommonCurrency, Symbols = new List<ExchangeSymbol>() };
                        if (request.Exchange.StartTime < request.Exchange.EndTime)
                        {
                            if (request.Exchange.CommonCurrency)
                            {
                                SqlTransaction transaction = null;
                                var connection = new SqlConnection(_connectionString);
                                try
                                {   
                                    connection.Open();
                                    transaction = connection.BeginTransaction();
                                    var command = connection.CreateCommand();
                                    command.Transaction = transaction;
                                    List<ExchangeSymbol> symbols = new List<ExchangeSymbol>();
                                    command.CommandText = "SELECT e.[Name], s.[ID], s.[Name], s.[Exchange], s.[Currency] FROM [Exchanges] e, [Symbols] s WHERE e.[Name] = @OldName AND e.[Name] = s.[Exchange] ORDER BY e.[Name]";
                                    command.Parameters.AddWithValue("OldName", request.OldExchangeName);
                                    var reader = command.ExecuteReader();
                                    if (reader.HasRows)
                                    {
                                        while (reader.Read())
                                        {
                                            exchange.Symbols.Add(new ExchangeSymbol() { ID = reader.GetString(1), Symbol = new Symbol() { Name = reader.GetString(2), Exchange = reader.GetString(3), Currency = reader.GetString(4) } });
                                        }
                                    }
                                    reader.Close();
                                    command = connection.CreateCommand();
                                    command.Transaction = transaction;
                                    command.CommandText = "UPDATE [Exchanges] SET [Name] = @NewName, [StartTime] = @StartTime, [EndTime] = @EndTime, [CommonCurrency] = @CommonCurrency WHERE [Name] = @OldName";
                                    command.Parameters.AddWithValue("NewName", request.Exchange.Name);
                                    command.Parameters.AddWithValue("StartTime", request.Exchange.StartTime);
                                    command.Parameters.AddWithValue("EndTime", request.Exchange.EndTime);
                                    command.Parameters.AddWithValue("CommonCurrency", request.Exchange.CommonCurrency);
                                    command.Parameters.AddWithValue("OldName", request.OldExchangeName);
                                    command.ExecuteNonQuery();
                                    transaction.Commit();
                                }
                                catch (Exception ex)
                                {
                                    if (transaction != null)
                                    {
                                        transaction.Rollback();
                                    }
                                    result = ex.Message;
                                }
                                if (connection.State == System.Data.ConnectionState.Open)
                                {
                                    connection.Close();
                                }
                            }
                            else
                            {
                                //modify record(s) in exchanges and symbols tables in single transaction
                                SqlTransaction transaction = null;
                                SqlConnection connection = new SqlConnection(_connectionString);
                                try
                                {
                                    connection.Open();
                                    transaction = connection.BeginTransaction();
                                    SqlCommand command = connection.CreateCommand();
                                    command.Transaction = transaction;
                                    List<string> currencies = new List<string>();
                                    command.CommandText = "SELECT [Name] FROM [Currencies]";
                                    var reader = command.ExecuteReader();
                                    if (reader.HasRows)
                                    {
                                        while (reader.Read())
                                        {
                                            currencies.Add(reader.GetString(0));
                                        }
                                    }
                                    reader.Close();
                                    command = connection.CreateCommand();
                                    command.Transaction = transaction;
                                    List<ExchangeSymbol> oldSymbols = new List<ExchangeSymbol>();
                                    command.CommandText = "SELECT e.[Name], s.[ID], s.[Name], s.[Exchange], s.[Currency] FROM [Exchanges] e, [Symbols] s WHERE e.[Name] = @OldName AND e.[Name] = s.[Exchange] ORDER BY e.[Name]";
                                    command.Parameters.AddWithValue("OldName", request.OldExchangeName);
                                    reader = command.ExecuteReader();
                                    if (reader.HasRows)
                                    {
                                        while (reader.Read())
                                        {
                                            oldSymbols.Add(new ExchangeSymbol() { ID = reader.GetString(1), Symbol = new Symbol() { Name = reader.GetString(2), Exchange = reader.GetString(3), Currency = reader.GetString(4) } });
                                        }
                                    }
                                    reader.Close();
                                    var symbolsToRemove = oldSymbols.Select(s => s.Symbol).Except(request.Exchange.Symbols, new SymbolComparer()).ToList();
                                    var symbolsToAdd = request.Exchange.Symbols.Except(oldSymbols.Select(s => s.Symbol), new SymbolComparer()).ToList();
                                    foreach (var symbol in symbolsToRemove)
                                    {
                                        command = connection.CreateCommand();
                                        command.Transaction = transaction;
                                        command.CommandText = "DELETE FROM [Symbols] WHERE [Name] = @Name AND [Exchange] = @Exchange AND [Currency] = @Currency";
                                        command.Parameters.AddWithValue("Name", symbol.Name);
                                        command.Parameters.AddWithValue("Exchange", symbol.Exchange);
                                        command.Parameters.AddWithValue("Currency", symbol.Currency);
                                        command.ExecuteNonQuery();
                                    }
                                    command = connection.CreateCommand();
                                    command.Transaction = transaction;
                                    command.CommandText = "UPDATE [Exchanges] SET [Name] = @NewName, [StartTime] = @StartTime, [EndTime] = @EndTime, [CommonCurrency] = @CommonCurrency WHERE [Name] = @OldName";
                                    command.Parameters.AddWithValue("NewName", request.Exchange.Name);
                                    command.Parameters.AddWithValue("StartTime", request.Exchange.StartTime);
                                    command.Parameters.AddWithValue("EndTime", request.Exchange.EndTime);
                                    command.Parameters.AddWithValue("CommonCurrency", request.Exchange.CommonCurrency);
                                    command.Parameters.AddWithValue("OldName", request.OldExchangeName);
                                    command.ExecuteNonQuery();
                                    DataTable table = new DataTable();
                                    table.Columns.Add("ID", typeof(string));
                                    table.Columns.Add("Name", typeof(string));
                                    table.Columns.Add("Exchange", typeof(string));
                                    table.Columns.Add("Currency", typeof(string));
                                    symbolsToAdd.ForEach(item =>
                                    {
                                        if (currencies.Contains(item.Currency))
                                        {
                                            ExchangeSymbol symbol = new ExchangeSymbol() { ID = Guid.NewGuid().ToString(), Symbol = new Symbol() { Name = item.Name, Exchange = item.Exchange, Currency = item.Currency } };
                                            table.Rows.Add(symbol.ID, symbol.Symbol.Name, symbol.Symbol.Exchange, symbol.Symbol.Currency);
                                            exchange.Symbols.Add(symbol);
                                        }
                                    });
                                    if (table.Rows.Count > 0)
                                    {
                                        SqlBulkCopy copy = new SqlBulkCopy(connection, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock, transaction);
                                        copy.DestinationTableName = "Symbols";
                                        copy.WriteToServer(table);
                                    }
                                    symbolsToRemove.ForEach(s => oldSymbols.RemoveAll(os => os.Symbol.Equals(s)));
                                    exchange.Symbols.AddRange(oldSymbols);
                                    if (exchange.Symbols.Count > 0)
                                    {
                                        command = connection.CreateCommand();
                                        command.Transaction = transaction;
                                        command.CommandText = string.Format("UPDATE [Orders] SET [ExpirationDate] = @ExpirationDate WHERE [SymbolID] in ({0})", string.Join(",", exchange.Symbols.Select(s => string.Format("'{0}'", s.ID))));
                                        command.Parameters.AddWithValue("ExpirationDate", DateTime.UtcNow.Date.Add(exchange.EndTime));
                                        command.ExecuteNonQuery();
                                        transaction.Commit();
                                    }
                                    else
                                    {
                                        result = "Invalid symbols";
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (transaction != null)
                                    {
                                        transaction.Rollback();
                                    }
                                    result = ex.Message;
                                }
                                if (connection.State == System.Data.ConnectionState.Open)
                                {
                                    connection.Close();
                                }
                            }
                        }
                        else
                        {
                            result = "Invalid exchange session time. Start time must be less then end time.";
                        }
                        session.Send(Serialize(new MessageResponse() { MessageID = request.MessageID, Result = result }));
                        if (string.IsNullOrEmpty(result))
                        {
                            //update users subscriptions
                            lock(_connectedUsers)
                            {
                                _connectedUsers.ForEach(u => { u.Subscribers.RemoveAll(s => !request.Exchange.Symbols.Contains(s.Symbol, new SymbolComparer())); });
                            }
                            //update exchanges in order matching engine 
                            _dataFeed.EditExchange(request.OldExchangeName, exchange);
                        }
                    }
                }
                else if (baseMessage.MessageType == "DeleteExchangeRequest")
                {
                    var request = Deserialize<DeleteExchangeRequest>(message);
                    if (request != null)
                    {
                        string result = null;
                        //delete record(s) from exchanges and symbols tables
                        SqlConnection connection = new SqlConnection(_connectionString);
                        try
                        {
                            connection.Open();
                            SqlCommand command = connection.CreateCommand();
                            command.CommandText = "DELETE FROM [Exchanges] WHERE [Name] = @Name";
                            command.Parameters.AddWithValue("Name", request.ExchangeName);
                            command.ExecuteNonQuery();
                        }
                        catch (Exception ex)
                        {
                            result = ex.Message;
                        }
                        if (connection.State == System.Data.ConnectionState.Open)
                        {
                            connection.Close();
                        }
                        session.Send(Serialize(new MessageResponse() { MessageID = request.MessageID, Result = result }));
                        if (string.IsNullOrEmpty(result))
                        {
                            //update users subscriptions
                            lock (_connectedUsers)
                            {
                                _connectedUsers.ForEach(u => { u.Subscribers.RemoveAll(s => s.Symbol.Exchange == request.ExchangeName); });
                            }
                            //update exchanges in order matching engine 
                            _dataFeed.DeleteExchange(request.ExchangeName);
                        }
                    }
                }
                else if (baseMessage.MessageType == "SetCurrenciesRequest")
                {
                    var request = Deserialize<SetCurrenciesRequest>(message);
                    if (request != null)
                    {
                        string result = null;
                        if (request.Currencies.Count > 0)
                        {
                            //modify record(s) in exchanges and symbols tables in single transaction
                            SqlTransaction transaction = null;
                            SqlConnection connection = new SqlConnection(_connectionString);
                            try
                            {
                                connection.Open();
                                SqlCommand command = connection.CreateCommand();
                                command.CommandText = "SELECT DISTINCT [Currency] FROM [Accounts]";
                                var reader = command.ExecuteReader();
                                List<string> accountCurrenies = new List<string>();
                                if (reader.HasRows)
                                {
                                    while (reader.Read())
                                    {
                                        accountCurrenies.Add(reader.GetString(0));
                                    }
                                    if (accountCurrenies.FirstOrDefault(ac => !request.Currencies.Select(c => c.Name).Contains(ac)) != null)
                                    {
                                        result = "Some users account refered to unexisting currencies, please update users first";
                                    }
                                }
                                reader.Close();
                                if (string.IsNullOrEmpty(result))
                                {
                                    command = connection.CreateCommand();
                                    command.CommandText = "SELECT [Name] FROM [Currencies]";
                                    reader = command.ExecuteReader();
                                    List<string> oldCurrenies = new List<string>();
                                    List<string> existingCurrenies = new List<string>();
                                    if (reader.HasRows)
                                    {
                                        while (reader.Read())
                                        {
                                            if (request.Currencies.FirstOrDefault(c => c.Name == reader.GetString(0)) == null)
                                            {
                                                oldCurrenies.Add(reader.GetString(0));
                                            }
                                            else
                                            {
                                                existingCurrenies.Add(reader.GetString(0));
                                            }
                                        }
                                    }
                                    reader.Close();
                                    transaction = connection.BeginTransaction();
                                    oldCurrenies.ForEach(c =>
                                    {
                                        command = connection.CreateCommand();
                                        command.Transaction = transaction;
                                        command.CommandText = "DELETE FROM [Currencies] WHERE [NAME] = @Name";
                                        command.Parameters.AddWithValue("Name", c);
                                        command.ExecuteNonQuery();
                                    });

                                    DataTable table = new DataTable();
                                    table.Columns.Add("Name", typeof(string));
                                    table.Columns.Add("Multiplier", typeof(decimal));
                                    request.Currencies.ForEach(c =>
                                    {
                                        if (!existingCurrenies.Contains(c.Name))
                                        {
                                            table.Rows.Add(c.Name, c.Multiplier);
                                        }
                                    });
                                    SqlBulkCopy copy = new SqlBulkCopy(connection, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock, transaction);
                                    copy.DestinationTableName = "Currencies";
                                    copy.WriteToServer(table);
                                    transaction.Commit();
                                }
                            }
                            catch (Exception ex)
                            {
                                if (transaction != null)
                                {
                                    transaction.Rollback();
                                }
                                result = ex.Message;
                            }
                            if (connection.State == System.Data.ConnectionState.Open)
                            {
                                connection.Close();
                            }
                        }
                        else
                        {
                            result = "Invalid currencies";
                        }
                        session.Send(Serialize(new MessageResponse() { MessageID = request.MessageID, Result = result }));
                        if (string.IsNullOrEmpty(result))
                        {
                            //update currencies list in order matching engine
                            _dataFeed.SetCurrencies(request.Currencies);
                        }
                    }
                }
                else if (baseMessage.MessageType == "ChangePasswordRequest")
                {
                    var request = Deserialize<ChangePasswordRequest>(message);
                    if (request != null)
                    {
                        string result = "";
                        //updates admin password in admin database
                        SqlConnection connection = new SqlConnection(_connectionString);
                        try
                        {
                            connection.Open();
                            SqlCommand command = connection.CreateCommand();
                            command.CommandText = "UPDATE [Admins] SET [Password] = @Password WHERE [UserName] = @UserName";
                            command.Parameters.AddWithValue("UserName", request.UserName);
                            command.Parameters.AddWithValue("Password", request.Password);
                            command.ExecuteNonQuery();
                        }
                        catch (Exception ex)
                        {
                            result = "Internal error on server, please try later";
                        }
                        if (connection.State == System.Data.ConnectionState.Open)
                        {
                            connection.Close();
                        }
                        session.Send(Serialize(new MessageResponse() { MessageID = request.MessageID, Result = result }));
                    }
                }
            }
        }

        //user session closed notification
        private void BrokerServerOnSessionClosed(WebSocketSession session, CloseReason r)
        {
            lock (_admins)
            {
                _connectedUsers.RemoveAll(a => a.Session == session);
            }
        }

        //handles exchange user messages 
        private void BrokerServerOnMessageReceived(WebSocketSession session, string message)
        {
            var baseMessage = Deserialize<Message>(message);
            if (baseMessage != null)
            {
                if (baseMessage.MessageType == "LoginRequest")
                {
                    var request = Deserialize<LoginRequest>(message);
                    if (request != null)
                    {
                        UserLoginResponse result = new UserLoginResponse() { LoginResult = LoginResult.InternalError, Symbols = new List<Symbol>(), Currencies = new List<string>(), Accounts = new List<Account>(), Orders = new List<NewOrderRequest>(), Executions = new List<Execution>() };
                        lock (_connectedUsers)
                        {
                            UserSession user = _connectedUsers.FirstOrDefault(u => u.UserName == request.UserName);
                            if (user != null)
                            {
                                _connectedUsers.Remove(user);
                                session.Send(Serialize(new ServerInfoMessage() { Info = "Another user logged in with the same credentials" }));
                                result.LoginResult = LoginResult.AlreadyLogged;
                            }
                            else
                            {
                                result = _dataFeed.Login(request);
                                if (result.LoginResult == LoginResult.OK)
                                {
                                    _connectedUsers.Add(new UserSession() { UserName = request.UserName, Subscribers = new List<SusbcibedItem>(), Session = session });
                                }
                            }
                        }
                        result.MessageID = request.MessageID;
                        session.Send(Serialize(result));
                    }
                }
                else if (baseMessage.MessageType == "SubscribeRequest")
                {
                    var request = Deserialize<SubscribeRequest>(message);
                    if (request != null)
                    {
                        lock (_connectedUsers)
                        {
                            UserSession user = _connectedUsers.FirstOrDefault(u => u.UserName == request.UserName);
                            if (user != null)
                            {
                                if (request.Subscribe)
                                {
                                    if (user.Subscribers.Count(s => s.Equals(request.Symbol)) == 0)
                                    {
                                        user.Subscribers.Add(new SusbcibedItem() { Symbol = request.Symbol, Currency = request.Currency });
                                        Tick tick = _dataFeed.GetLastTick(request.Symbol, request.Currency);
                                        if (tick != null)
                                        {
                                            session.Send(Serialize(tick));
                                        }
                                    }
                                }
                                else
                                {
                                    user.Subscribers.RemoveAll(s => s.Equals(request.Symbol));
                                }
                            }
                        }
                    }
                }
                else if (baseMessage.MessageType == "HistoryRequest")
                {
                    var request = Deserialize<HistoryRequest>(message);
                    if (request != null)
                    {
                        ThreadPool.QueueUserWorkItem(o =>
                        {
                            HistoryRequest parameters = (HistoryRequest)o;
                            var bars = _dataFeed.GetHistory(parameters.Parameters);
                            lock (_connectedUsers)
                            {
                                UserSession user = _connectedUsers.FirstOrDefault(u => u.UserName == parameters.UserName);
                                if (user != null)
                                {
                                    user.Session.Send(Serialize(new HistoryResponse() { Bars = bars, MessageID = parameters.MessageID }));
                                }
                            }
                        }, request);
                    }
                }
                else if (baseMessage.MessageType == "NewOrderRequest")
                {
                    var request = Deserialize<NewOrderRequest>(message);
                    if (request != null)
                    {
                        _dataFeed.ProcessRequest(request);
                    }
                }
                else if (baseMessage.MessageType == "ModifyOrderRequest")
                {
                    var request = Deserialize<ModifyOrderRequest>(message);
                    if (request != null)
                    {
                        _dataFeed.ProcessRequest(request);
                    }
                }
                else if (baseMessage.MessageType == "CancelOrderRequest")
                {
                    var request = Deserialize<CancelOrderRequest>(message);
                    if (request != null)
                    {
                        _dataFeed.ProcessRequest(request);
                    }
                }
                else if (baseMessage.MessageType == "LogoutRequest")
                {
                    var request = Deserialize<LogoutRequest>(message);
                    if (request != null)
                    {
                        _connectedUsers.Remove(_connectedUsers.FirstOrDefault(u => u.UserName == request.UserName));
                    }
                }
            }
        }

        //helper function to serialize network protocl messages
        private string Serialize<T>(T obj)
        {
            string result = "";
            var memoryStream = new MemoryStream();
            var serializer = new DataContractJsonSerializer(obj.GetType());
            try
            {
                serializer.WriteObject(memoryStream, obj);
                memoryStream.Position = 0;
                var streamReader = new StreamReader(memoryStream);
                result = streamReader.ReadToEnd();
                streamReader.Dispose();
            }
            catch { }
            memoryStream.Dispose();
            return result;
        }

        //helper function to deserialize network protocl messages
        private T Deserialize<T>(string data)
        {
            T result = default(T);
            var memoryStream = new MemoryStream();
            var streamWriter = new StreamWriter(memoryStream);
            try
            {
                streamWriter.Write(data);
                streamWriter.Flush();
                memoryStream.Position = 0;
                var ser = new DataContractJsonSerializer(typeof(T));
                result = (T)ser.ReadObject(memoryStream);
            }
            catch { }
            memoryStream.Dispose();
            streamWriter.Dispose();
            return result;
        }
    }
}