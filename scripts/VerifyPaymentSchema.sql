USE VietVanDb;
SELECT name AS TableName FROM sys.tables WHERE name IN
('Users','SubscriptionPackages','UserSubscriptions','RevenueTransactions','PaymentOrders',
'PaymentBankTransactions','PaymentWebhookEvents','PaymentAuditLogs','DramaProductions');
SELECT name,Price,DurationInDays,TierLevel,IsActive FROM dbo.SubscriptionPackages;
SELECT COUNT(*) AS OrderCount FROM dbo.PaymentOrders;
SELECT COUNT(*) AS WebhookCount FROM dbo.PaymentWebhookEvents;
