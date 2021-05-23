SELECT o.[ID], o.[AccountID], o.[SymbolID], o.[Time], o.[ActivationTime], o.[Side], o.[OrderType], o.[LimitPrice], o.[StopPrice], o.[Quantity], o.[TimeInForce], o.[ExpirationDate], e.[Time], e.[Status], e.[LastPrice], e.[LastQuantity], e.[FilledQuantity], e.[LeaveQuantity], e.[AverrageFillPrice] FROM [Orders] o, [Executions] e WHERE o.ID = e.[OrderID] 
 


 select * from orders where id='34d1be92-6974-4621-b63f-ecdc0c585df1'


 alter table Executions alter column LastQuantity decimal(18,2) not null
  alter table Executions alter column FilledQuantity decimal(18,2) not null
   alter table Executions alter column LeaveQuantity decimal(18,2) not null
    alter table Executions alter column CancelledQuantity decimal(18,2) not null
	 alter table Orders alter column Quantity decimal(18,2) not null