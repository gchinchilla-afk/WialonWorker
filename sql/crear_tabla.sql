-- Resumen diario por unidad que extrae WialonWorker. Correr una vez en ApisDB.
IF OBJECT_ID('dbo.WialonResumenDiario') IS NULL
CREATE TABLE dbo.WialonResumenDiario (
    Fecha            date          NOT NULL,
    UnidadId         bigint        NOT NULL,  -- id de la unidad en Wialon
    Unidad           nvarchar(100) NOT NULL,
    HorasMotor       decimal(9,2)  NULL,
    HorasRalenti     decimal(9,2)  NULL,
    HorasTraslado    decimal(9,2)  NULL,
    Combustible      decimal(12,2) NULL,      -- litros (según la cuenta de Wialon)
    VelocidadMaxima  decimal(7,2)  NULL,      -- km/h
    VelocidadMedia   decimal(7,2)  NULL,      -- km/h
    Distancia        decimal(12,2) NULL,      -- km
    FechaRegistro    datetime2(0)  NOT NULL CONSTRAINT DF_WialonResumenDiario_FechaRegistro DEFAULT SYSDATETIME(),
    CONSTRAINT PK_WialonResumenDiario PRIMARY KEY (Fecha, UnidadId)
);
