/* WARNING! This program and source code is owned and licensed by 
   Modulus Financial Engineering, Inc. http://www.modulusfe.com
   Viewing or use this code requires your acceptance of the license
   agreement found at http://www.modulusfe.com/support/license.pdf
   Removal of this comment is a violation of the license agreement.
   Copyright 2002-2016 by Modulus Financial Engineering, Inc. */

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace NDAXCore
{
    public enum LoginResult
    {
        OK,
        InvalidCredentials,
        AlreadyLogged,
        InternalError,
    }

    //historical request periodicity 
    public enum Periodicity
    {
        Tick = 0,
        Secondly = 1,
        Minutely = 2,
        Hourly = 3,
        Daily = 4,
        Weekly = 5,
        Monthly = 6,
        Yearly = 7
    }

    //order duration
    public enum TimeInForce
    {
        DAY,
        GTC,
        GTD,
        FOK,
        AON,
        IOC
    }

    //order type
    public enum Type
    {
        Market,
        Stop,
        Limit,
        StopLimit
    }

    //order side
    public enum Side
    {
        Buy,
        Sell
    }

    //order status
    public enum Status
    {
        Opened,
        Activated,
        PartialFilled,
        Filled,
        Done,
        Rejected,
        Cancelled,
        Expiried
    }

    //base class for protocol messages
    [DataContract]
    public class Message
    {
        [DataMember]
        public string MessageType;
        [DataMember]
        public string MessageID = Guid.NewGuid().ToString();
    }

    [DataContract]
    public class AdminLoginResponse : Message
    {
        [DataMember]
        public LoginResult AdminLoginResult;
        //list of available exchanges
        [DataMember]
        public List<ExchangeSettings> Exchanges;
        //list of available currencies
        [DataMember]
        public List<Currency> Currencies;
        //list of users/acounts
        [DataMember]
        public List<User> Users;

        public AdminLoginResponse()
        {
            MessageType = "AdminLoginResponse";
        }
    }

    //trading hours and symbols of exchange
    [DataContract]
    public partial class ExchangeSettings
    {
        [DataMember]
        public string Name;
        [DataMember]
        public TimeSpan StartTime;
        [DataMember]
        public TimeSpan EndTime;
        [DataMember]
        public List<Symbol> Symbols;
        [DataMember]
        public bool CommonCurrency;
    }

    [DataContract]
    public class AddUserRequest : Message
    {
        [DataMember]
        public User User;

        public AddUserRequest()
        {
            MessageType = "AddUserRequest";
        }
    }

    [DataContract]
    public class EditUserRequest : Message
    {
        [DataMember]
        public User User;

        [DataMember]
        public string OldUserName;

        public EditUserRequest()
        {
            MessageType = "EditUserRequest";
        }
    }

    [DataContract]
    public class DeleteUserRequest : Message
    {
        [DataMember]
        public string UserName;

        public DeleteUserRequest()
        {
            MessageType = "DeleteUserRequest";
        }
    }

    [DataContract]
    public class AddExchangeRequest : Message
    {
        [DataMember]
        public ExchangeSettings Exchange;

        public AddExchangeRequest()
        {
            MessageType = "AddExchangeRequest";
        }
    }

    [DataContract]
    public class EditExchangeRequest : Message
    {
        [DataMember]
        public ExchangeSettings Exchange;

        [DataMember]
        public string OldExchangeName;

        public EditExchangeRequest()
        {
            MessageType = "EditExchangeRequest";
        }
    }

    [DataContract]
    public class DeleteExchangeRequest : Message
    {
        [DataMember]
        public string ExchangeName;

        public DeleteExchangeRequest()
        {
            MessageType = "DeleteExchangeRequest";
        }
    }

    [DataContract]
    public class SetCurrenciesRequest : Message
    {
        [DataMember]
        public List<Currency> Currencies;

        public SetCurrenciesRequest()
        {
            MessageType = "SetCurrenciesRequest";
        }
    }

    [DataContract]
    public class ChangePasswordRequest : Message
    {
        [DataMember]
        public string UserName;
        [DataMember]
        public string Password;

        public ChangePasswordRequest()
        {
            MessageType = "ChangePasswordRequest";
        }
    }

    [DataContract]
    public class LoginRequest : Message
    {
        [DataMember]
        public string UserName;
        [DataMember]
        public string Password;

        public LoginRequest()
        {
            MessageType = "LoginRequest";
        }
    }

    [DataContract]
    public class UserLoginResponse : Message
    {
        [DataMember]
        public LoginResult LoginResult;
        //list of available for trading symbols
        [DataMember]
        public List<Symbol> Symbols;
        //list of available currencies
        [DataMember]
        public List<string> Currencies;
        //list of user accounts
        [DataMember]
        public List<Account> Accounts;
        //list of user orders
        [DataMember]
        public List<NewOrderRequest> Orders;
        //list of user orders executions
        [DataMember]
        public List<Execution> Executions;

        public UserLoginResponse()
        {
            MessageType = "UserLoginResponse";
        }
    }

    [DataContract]
    public class SubscribeRequest : Message
    {
        [DataMember]
        public string UserName;
        [DataMember]
        public Symbol Symbol;
        [DataMember]
        public string Currency;
        //subscribe or unsubscribe
        [DataMember]
        public bool Subscribe;

        public SubscribeRequest()
        {
            MessageType = "SubscribeRequest";
        }
    }

    [DataContract]
    public partial class Symbol
    {
        [DataMember]
        public string Name;
        [DataMember]
        public string Exchange;
        [DataMember]
        public string Currency;
    }

    [DataContract]
    public class User
    {
        [DataMember]
        public string UserName;
        [DataMember]
        public string Password;
        [DataMember]
        public List<Account> Accounts;
    }

    [DataContract]
    public partial class Currency
    {
        [DataMember]
        public string Name;
        [DataMember]
        public decimal Multiplier;
    }

    [DataContract]
    public class Account : Message
    {
        [DataMember]
        public string Name;
        [DataMember]
        public decimal Balance;
        [DataMember]
        public string Currency;

        public Account()
        {
            MessageType = "Account";
        }
    }

    [DataContract]
    public class HistoryParameters
    {
        [DataMember]
        public Symbol Symbol;
        [DataMember]
        public string Currency;
        [DataMember]
        public Periodicity Periodicity;
        [DataMember]
        public int Interval;
        [DataMember]
        public int BarsCount;
    }

    [DataContract]
    public class HistoryRequest : Message
    {
        [DataMember]
        public string UserName;
        [DataMember]
        public HistoryParameters Parameters;

        public HistoryRequest()
        {
            MessageType = "HistoryRequest";
        }
    }

    [DataContract]
    public class HistoryResponse : Message
    {
        [DataMember]
        public List<Bar> Bars;

        public HistoryResponse()
        {
            MessageType = "HistoryResponse";
        }
    }

    [DataContract]
    public partial class NewOrderRequest : Message
    {
        [DataMember]
        public string ID;
        [DataMember]
        public Symbol Symbol;
        [DataMember]
        public DateTime Time;
        public DateTime ActivationTime;
        [DataMember]
        public string Account;
        [DataMember]
        public Side Side;
        [DataMember]
        public Type OrderType;
        [DataMember]
        public decimal LimitPrice;
        [DataMember]
        public decimal StopPrice;
        [DataMember]
        public decimal Quantity;
        [DataMember]
        public TimeInForce TimeInForce;
        [DataMember]
        public DateTime ExpirationDate;

        public NewOrderRequest()
        {
            MessageType = "NewOrderRequest";
        }
    }

    [DataContract]
    public class ModifyOrderRequest : Message
    {
        [DataMember]
        public string ID;
        [DataMember]
        public decimal Quantity;
        [DataMember]
        public Type OrderType;
        [DataMember]
        public decimal LimitPrice;
        [DataMember]
        public decimal StopPrice;
        [DataMember]
        public TimeInForce TimeInForce;
        [DataMember]
        public DateTime ExpirationDate;

        public ModifyOrderRequest()
        {
            MessageType = "ModifyOrderRequest";
        }
    }

    [DataContract]
    public class CancelOrderRequest : Message
    {
        [DataMember]
        public string ID;

        public CancelOrderRequest()
        {
            MessageType = "CancelOrderRequest";
        }
    }

    [DataContract]
    public partial class Tick : Message
    {
        [DataMember]
        public Symbol Symbol;
        [DataMember]
        public string Currency;
        [DataMember]
        public DateTime Time;
        [DataMember]
        public decimal Bid;
        [DataMember]
        public decimal BidSize;
        [DataMember]
        public decimal Ask;
        [DataMember]
        public decimal AskSize;
        [DataMember]
        public decimal Price;
        [DataMember]
        public decimal Volume;

        public Tick()
        {
            MessageType = "Tick";
        }
    }

    public partial class Bar
    {
        [DataMember]
        public DateTime Time;
        [DataMember]
        public decimal Open;
        [DataMember]
        public decimal High;
        [DataMember]
        public decimal Low;
        [DataMember]
        public decimal Close;
        [DataMember]
        public decimal Volume;
        public DateTime EndDate { get; private set; }
    }

    //order execution 
    [DataContract]
    public partial class Execution : Message
    {
        [DataMember]
        public string OrderID;
        [DataMember]
        public DateTime Time;
        [DataMember]
        public Status Status;
        [DataMember]
        public decimal LastPrice;
        [DataMember]
        public decimal LastQuantity;
        [DataMember]
        public decimal FilledQuantity;
        [DataMember]
        public decimal LeaveQuantity;
        [DataMember]
        public decimal CancelledQuantity;
        [DataMember]
        public decimal AverrageFillPrice;
        [DataMember]
        public string Message;

        public Execution()
        {
            MessageType = "Execution";
        }

        public Execution(string orderID, DateTime time, Status status, decimal lastPrice, decimal lastQuantity, decimal filledQuantity, decimal leaveQuantity, decimal cancelledQuantity, string message)
            : base()
        {
            MessageType = "Execution";
            OrderID = orderID;
            Time = time;
            Status = status;
            LastPrice = lastPrice;
            LastQuantity = lastQuantity;
            FilledQuantity = filledQuantity;
            LeaveQuantity = leaveQuantity;
            AverrageFillPrice = lastPrice;
            Message = message;
            CancelledQuantity = cancelledQuantity;
        }
    }

    [DataContract]
    public class LogoutRequest : Message
    {
        [DataMember]
        public string UserName;

        public LogoutRequest()
        {
            MessageType = "LogoutRequest";
        }
    }

    [DataContract]
    public class MessageResponse : Message
    {
        [DataMember]
        public string Result;

        public MessageResponse()
        {
            MessageType = "MessageResponse";
        }
    }

    [DataContract]
    public class ServerInfoMessage : Message
    {
        [DataMember]
        public string Info;

        [DataMember]
        public bool Exit;

        public ServerInfoMessage()
        {
            MessageType = "ServerInfoMessage";
        }
    }
}