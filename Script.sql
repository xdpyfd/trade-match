USE [master]
GO
/****** Object:  Database [NDAXCoreEx]    Script Date: 9/4/2015 4:34:38 PM ******/
CREATE DATABASE [NDAXCoreEx]
GO
USE [NDAXCoreEx]
GO
/****** Object:  Table [dbo].[Accounts]    Script Date: 9/4/2015 4:34:38 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Accounts](
	[UserName] [nvarchar](50) NOT NULL,
	[Name] [nvarchar](50) NOT NULL,
	[Balance] [decimal](38, 8) NOT NULL,
	[Currency] [nvarchar](50) NOT NULL,
 CONSTRAINT [PK_Account_1] PRIMARY KEY CLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
/****** Object:  Table [dbo].[Admins]    Script Date: 9/4/2015 4:34:38 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Admins](
	[UserName] [nvarchar](50) NOT NULL,
	[Password] [nvarchar](50) NOT NULL,
 CONSTRAINT [PK_Admins] PRIMARY KEY CLUSTERED 
(
	[UserName] ASC,
	[Password] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
/****** Object:  Table [dbo].[Currencies]    Script Date: 9/4/2015 4:34:38 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Currencies](
	[Name] [nvarchar](50) NOT NULL,
	[Multiplier] [decimal](18, 8) NOT NULL,
 CONSTRAINT [PK_Currencies] PRIMARY KEY CLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
/****** Object:  Table [dbo].[Daily]    Script Date: 9/4/2015 4:34:38 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Daily](
	[SymbolID] [nvarchar](50) NOT NULL,
	[Time] [datetime] NOT NULL,
	[Open] [decimal](18, 8) NOT NULL,
	[High] [decimal](18, 8) NOT NULL,
	[Low] [decimal](18, 8) NOT NULL,
	[Close] [decimal](18, 8) NOT NULL,
	[Volume] [bigint] NOT NULL,
 CONSTRAINT [PK_Daily_1] PRIMARY KEY CLUSTERED 
(
	[SymbolID] ASC,
	[Time] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
/****** Object:  Table [dbo].[Exchanges]    Script Date: 9/4/2015 4:34:38 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Exchanges](
	[Name] [nvarchar](50) NOT NULL,
	[StartTime] [time](7) NOT NULL,
	[EndTime] [time](7) NOT NULL,
	[CommonCurrency] [bit] NOT NULL,
 CONSTRAINT [PK_Exchange] PRIMARY KEY CLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
/****** Object:  Table [dbo].[Executions]    Script Date: 9/4/2015 4:34:38 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Executions](
	[OrderID] [nvarchar](50) NOT NULL,
	[Time] [datetime] NOT NULL,
	[Status] [tinyint] NOT NULL,
	[LastPrice] [decimal](18, 8) NOT NULL,
	[LastQuantity] [bigint] NOT NULL,
	[FilledQuantity] [bigint] NOT NULL,
	[LeaveQuantity] [bigint] NOT NULL,
	[CancelledQuantity] [bigint] NOT NULL,
	[AverrageFillPrice] [decimal](18, 8) NOT NULL,
 CONSTRAINT [PK_Executions_1] PRIMARY KEY CLUSTERED 
(
	[OrderID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
/****** Object:  Table [dbo].[Minutely]    Script Date: 9/4/2015 4:34:38 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Minutely](
	[SymbolID] [nvarchar](50) NOT NULL,
	[Time] [datetime] NOT NULL,
	[Open] [decimal](18, 8) NOT NULL,
	[High] [decimal](18, 8) NOT NULL,
	[Low] [decimal](18, 8) NOT NULL,
	[Close] [decimal](18, 8) NOT NULL,
	[Volume] [bigint] NOT NULL,
 CONSTRAINT [PK_Minutely_1] PRIMARY KEY CLUSTERED 
(
	[SymbolID] ASC,
	[Time] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
/****** Object:  Table [dbo].[Orders]    Script Date: 9/4/2015 4:34:38 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Orders](
	[ID] [nvarchar](50) NOT NULL,
	[AccountID] [nvarchar](50) NOT NULL,
	[SymbolID] [nvarchar](50) NOT NULL,
	[Time] [datetime] NOT NULL,
	[ActivationTime] [datetime] NULL,
	[Side] [tinyint] NOT NULL,
	[OrderType] [tinyint] NOT NULL,
	[LimitPrice] [decimal](18, 8) NOT NULL,
	[StopPrice] [decimal](18, 8) NOT NULL,
	[Quantity] [bigint] NOT NULL,
	[TimeInForce] [tinyint] NOT NULL,
	[ExpirationDate] [datetime] NOT NULL,
 CONSTRAINT [PK_Orders_1] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
/****** Object:  Table [dbo].[Symbols]    Script Date: 9/4/2015 4:34:38 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Symbols](
	[ID] [nvarchar](50) NOT NULL,
	[Name] [nvarchar](50) NOT NULL,
	[Exchange] [nvarchar](50) NOT NULL,
	[Currency] [nvarchar](50) NOT NULL,
 CONSTRAINT [PK_Symbols] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [IX_Symbols] UNIQUE NONCLUSTERED 
(
	[Exchange] ASC,
	[Currency] ASC,
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
/****** Object:  Table [dbo].[Tick]    Script Date: 9/4/2015 4:34:38 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Tick](
	[SymbolID] [nvarchar](50) NOT NULL,
	[Time] [datetime] NOT NULL,
	[Price] [decimal](18, 8) NOT NULL,
	[Volume] [bigint] NOT NULL,
 CONSTRAINT [PK_Tick_1] PRIMARY KEY CLUSTERED 
(
	[SymbolID] ASC,
	[Time] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
/****** Object:  Table [dbo].[Users]    Script Date: 9/4/2015 4:34:38 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Users](
	[UserName] [nvarchar](50) NOT NULL,
	[Password] [nvarchar](50) NOT NULL,
 CONSTRAINT [PK_User] PRIMARY KEY CLUSTERED 
(
	[UserName] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]

GO
ALTER TABLE [dbo].[Accounts]  WITH CHECK ADD  CONSTRAINT [FK_Account_User] FOREIGN KEY([UserName])
REFERENCES [dbo].[Users] ([UserName])
ON UPDATE CASCADE
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Accounts] CHECK CONSTRAINT [FK_Account_User]
GO
ALTER TABLE [dbo].[Accounts]  WITH CHECK ADD  CONSTRAINT [FK_Accounts_Currencies] FOREIGN KEY([Currency])
REFERENCES [dbo].[Currencies] ([Name])
ON UPDATE CASCADE
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Accounts] CHECK CONSTRAINT [FK_Accounts_Currencies]
GO
ALTER TABLE [dbo].[Daily]  WITH CHECK ADD  CONSTRAINT [FK_Daily_Symbols] FOREIGN KEY([SymbolID])
REFERENCES [dbo].[Symbols] ([ID])
ON UPDATE CASCADE
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Daily] CHECK CONSTRAINT [FK_Daily_Symbols]
GO
ALTER TABLE [dbo].[Executions]  WITH CHECK ADD  CONSTRAINT [FK_Executions_Orders] FOREIGN KEY([OrderID])
REFERENCES [dbo].[Orders] ([ID])
ON UPDATE CASCADE
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Executions] CHECK CONSTRAINT [FK_Executions_Orders]
GO
ALTER TABLE [dbo].[Minutely]  WITH CHECK ADD  CONSTRAINT [FK_Minutely_Symbols] FOREIGN KEY([SymbolID])
REFERENCES [dbo].[Symbols] ([ID])
ON UPDATE CASCADE
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Minutely] CHECK CONSTRAINT [FK_Minutely_Symbols]
GO
ALTER TABLE [dbo].[Orders]  WITH CHECK ADD  CONSTRAINT [FK_Orders_Accounts1] FOREIGN KEY([AccountID])
REFERENCES [dbo].[Accounts] ([Name])
ON UPDATE CASCADE
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Orders] CHECK CONSTRAINT [FK_Orders_Accounts1]
GO
ALTER TABLE [dbo].[Orders]  WITH CHECK ADD  CONSTRAINT [FK_Orders_Symbols1] FOREIGN KEY([SymbolID])
REFERENCES [dbo].[Symbols] ([ID])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Orders] CHECK CONSTRAINT [FK_Orders_Symbols1]
GO
ALTER TABLE [dbo].[Symbols]  WITH CHECK ADD  CONSTRAINT [FK_Symbol_Exchange] FOREIGN KEY([Exchange])
REFERENCES [dbo].[Exchanges] ([Name])
ON UPDATE CASCADE
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Symbols] CHECK CONSTRAINT [FK_Symbol_Exchange]
GO
ALTER TABLE [dbo].[Symbols]  WITH CHECK ADD  CONSTRAINT [FK_Symbols_Currencies] FOREIGN KEY([Currency])
REFERENCES [dbo].[Currencies] ([Name])
GO
ALTER TABLE [dbo].[Symbols] CHECK CONSTRAINT [FK_Symbols_Currencies]
GO
ALTER TABLE [dbo].[Tick]  WITH CHECK ADD  CONSTRAINT [FK_Tick_Symbols] FOREIGN KEY([SymbolID])
REFERENCES [dbo].[Symbols] ([ID])
ON UPDATE CASCADE
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Tick] CHECK CONSTRAINT [FK_Tick_Symbols]
GO
USE [master]
GO
ALTER DATABASE [NDAXCoreEx] SET  READ_WRITE 
GO
