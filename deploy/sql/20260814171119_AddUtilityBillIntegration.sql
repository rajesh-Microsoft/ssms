BEGIN TRANSACTION;
CREATE TABLE [UtilityProviders] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(40) NOT NULL,
    [ProviderName] nvarchar(100) NOT NULL,
    [Category] nvarchar(40) NOT NULL,
    [SupportsAutoFetch] bit NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_UtilityProviders] PRIMARY KEY ([Id])
);

CREATE TABLE [UtilityConnections] (
    [Id] int NOT NULL IDENTITY,
    [ProviderId] int NOT NULL,
    [ConsumerNumber] nvarchar(80) NOT NULL,
    [ServiceNumber] nvarchar(80) NULL,
    [AutoFetchEnabled] bit NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [CreatedOn] datetime2 NOT NULL,
    [LastFetchedOn] datetime2 NULL,
    [LastFetchError] nvarchar(500) NULL,
    CONSTRAINT [PK_UtilityConnections] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UtilityConnections_UtilityProviders_ProviderId] FOREIGN KEY ([ProviderId]) REFERENCES [UtilityProviders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [UtilityBills] (
    [Id] int NOT NULL IDENTITY,
    [UtilityConnectionId] int NOT NULL,
    [BillingMonth] datetime2 NOT NULL,
    [BillNumber] nvarchar(100) NULL,
    [BillDate] datetime2 NULL,
    [DueDate] datetime2 NULL,
    [BillAmount] decimal(12,2) NOT NULL,
    [UnitsConsumed] decimal(12,2) NULL,
    [Arrears] decimal(12,2) NOT NULL,
    [ConsumerName] nvarchar(200) NULL,
    [Status] nvarchar(30) NOT NULL,
    [RawHtml] nvarchar(max) NOT NULL,
    [FetchedOn] datetime2 NOT NULL,
    CONSTRAINT [PK_UtilityBills] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UtilityBills_UtilityConnections_UtilityConnectionId] FOREIGN KEY ([UtilityConnectionId]) REFERENCES [UtilityConnections] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [UtilityNotifications] (
    [Id] int NOT NULL IDENTITY,
    [UtilityBillId] int NOT NULL,
    [Title] nvarchar(120) NOT NULL,
    [Message] nvarchar(500) NOT NULL,
    [CreatedOn] datetime2 NOT NULL,
    CONSTRAINT [PK_UtilityNotifications] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UtilityNotifications_UtilityBills_UtilityBillId] FOREIGN KEY ([UtilityBillId]) REFERENCES [UtilityBills] ([Id]) ON DELETE CASCADE
);

CREATE UNIQUE INDEX [IX_UtilityBills_UtilityConnectionId_BillingMonth] ON [UtilityBills] ([UtilityConnectionId], [BillingMonth]);

CREATE UNIQUE INDEX [IX_UtilityConnections_ProviderId_ConsumerNumber] ON [UtilityConnections] ([ProviderId], [ConsumerNumber]);

CREATE INDEX [IX_UtilityNotifications_UtilityBillId] ON [UtilityNotifications] ([UtilityBillId]);

CREATE UNIQUE INDEX [IX_UtilityProviders_Code] ON [UtilityProviders] ([Code]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260814171119_AddUtilityBillIntegration', N'10.0.0');

COMMIT;
GO

