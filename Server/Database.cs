using System;
using Microsoft.Data.SqlClient;

namespace FitnessServer
{
    public enum InstanceRole { Primary, Replica }

    public class InstanceUnavailableException : Exception
    {
        public InstanceUnavailableException(string message) : base(message) { }
    }

    public static class Database
    {
        // Блокировка для предотвращения конфликтов при параллельном запуске тестов
        private static readonly object _dbLock = new object();

        public static string PrimaryConnectionString { get; set; } =
            BuildConnectionString(GetEnv("DB_PRIMARY_NAME", "FitnessDB_Primary"));

        public static string ReplicaConnectionString { get; set; } =
            BuildConnectionString(GetEnv("DB_REPLICA_NAME", "FitnessDB_Replica"));

        private static string GetEnv(string name, string fallback) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

        private static string BuildConnectionString(string dbName)
        {
            var host = GetEnv("DB_HOST", "localhost");
            var port = GetEnv("DB_PORT", "14330");
            var user = GetEnv("DB_USER", "sa");
            var password = GetEnv("DB_PASSWORD", "YourStrong@Password123");

            return $"Server={host},{port};Database={dbName};User Id={user};" +
                   $"Password={password};TrustServerCertificate=True;Connect Timeout=5;";
        }

        public static InstanceRole ActiveRole { get; private set; } = InstanceRole.Primary;

        private static bool _primaryAvailable = true;
        private static bool _replicaAvailable = true;

        public static string GetConnectionString(InstanceRole role) =>
            role == InstanceRole.Primary ? PrimaryConnectionString : ReplicaConnectionString;

        private static string GetMasterConnectionString(string connStr)
        {
            var b = new SqlConnectionStringBuilder(connStr);
            b.InitialCatalog = "master";
            return b.ConnectionString;
        }

        public static bool IsAvailable(InstanceRole role)
        {
            if (role == InstanceRole.Primary && !_primaryAvailable) return false;
            if (role == InstanceRole.Replica && !_replicaAvailable) return false;

            try
            {
                using var conn = new SqlConnection(GetConnectionString(role));
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                cmd.ExecuteScalar();
                return true;
            }
            catch { return false; }
        }

        public static SqlConnection OpenConnection(InstanceRole role)
        {
            if (!IsAvailable(role))
                throw new InstanceUnavailableException($"Экземпляр {role} недоступен.");
            var conn = new SqlConnection(GetConnectionString(role));
            conn.Open();
            return conn;
        }

        public static SqlConnection OpenConnection() => OpenConnection(ActiveRole);

        public static void FailoverToReplica()
        {
            if (!IsAvailable(InstanceRole.Replica))
                throw new InstanceUnavailableException("Replica недоступна, failover невозможен.");

            ActiveRole = InstanceRole.Replica;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [FAILOVER] Переключение на Replica");
        }

        public static void RecoverPrimary()
        {
            _primaryAvailable = true;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [RECOVERY] Primary -> AVAILABLE");

            if (IsAvailable(InstanceRole.Primary) && IsAvailable(InstanceRole.Replica))
            {
                if (ActiveRole == InstanceRole.Replica)
                {
                    try { SyncReplicaToPrimary(); }
                    catch (Exception ex) { Console.WriteLine($"[RECOVERY] Ошибка синхронизации: {ex.Message}"); }
                }
            }

            ActiveRole = InstanceRole.Primary;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [RECOVERY] Переключение на Primary");
        }

        public static void SimulateFailure(InstanceRole role)
        {
            if (role == InstanceRole.Primary) _primaryAvailable = false;
            else _replicaAvailable = false;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [FAILURE] {role} -> UNAVAILABLE");
        }

        public static void SimulateRecovery(InstanceRole role)
        {
            if (role == InstanceRole.Primary) _primaryAvailable = true;
            else _replicaAvailable = true;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [RECOVERY] {role} -> AVAILABLE");
        }

        public static void SyncPrimaryToReplica() => SyncDatabase(InstanceRole.Primary, InstanceRole.Replica);
        public static void SyncReplicaToPrimary() => SyncDatabase(InstanceRole.Replica, InstanceRole.Primary);

        private static void SyncDatabase(InstanceRole source, InstanceRole target)
        {
            var srcConn = GetConnectionString(source);
            var tgtConn = GetConnectionString(target);
            var sourceDb = new SqlConnectionStringBuilder(srcConn).InitialCatalog;
            var targetDb = new SqlConnectionStringBuilder(tgtConn).InitialCatalog;
            var masterConnStr = GetMasterConnectionString(srcConn);

            using var conn = new SqlConnection(masterConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = $@"
                DELETE FROM [{targetDb}].dbo.Payments;
                DELETE FROM [{targetDb}].dbo.Memberships;
                DELETE FROM [{targetDb}].dbo.Trainings;
                DELETE FROM [{targetDb}].dbo.TrainingSlots;
                DELETE FROM [{targetDb}].dbo.Clients;
                DELETE FROM [{targetDb}].dbo.Trainers;

                SET IDENTITY_INSERT [{targetDb}].dbo.Trainers ON;
                INSERT INTO [{targetDb}].dbo.Trainers (Id, Name, Specialization)
                SELECT Id, Name, Specialization FROM [{sourceDb}].dbo.Trainers;
                SET IDENTITY_INSERT [{targetDb}].dbo.Trainers OFF;

                SET IDENTITY_INSERT [{targetDb}].dbo.Clients ON;
                INSERT INTO [{targetDb}].dbo.Clients (Id, Name, Email, Phone)
                SELECT Id, Name, Email, Phone FROM [{sourceDb}].dbo.Clients;
                SET IDENTITY_INSERT [{targetDb}].dbo.Clients OFF;

                SET IDENTITY_INSERT [{targetDb}].dbo.TrainingSlots ON;
                INSERT INTO [{targetDb}].dbo.TrainingSlots (Id, TrainerId, DateTime, AvailablePlaces)
                SELECT Id, TrainerId, DateTime, AvailablePlaces FROM [{sourceDb}].dbo.TrainingSlots;
                SET IDENTITY_INSERT [{targetDb}].dbo.TrainingSlots OFF;

                SET IDENTITY_INSERT [{targetDb}].dbo.Trainings ON;
                INSERT INTO [{targetDb}].dbo.Trainings (Id, ClientId, TrainerId, DateTime, Duration, Status)
                SELECT Id, ClientId, TrainerId, DateTime, Duration, Status FROM [{sourceDb}].dbo.Trainings;
                SET IDENTITY_INSERT [{targetDb}].dbo.Trainings OFF;

                SET IDENTITY_INSERT [{targetDb}].dbo.Memberships ON;
                INSERT INTO [{targetDb}].dbo.Memberships (Id, ClientId, Type, StartDate, EndDate)
                SELECT Id, ClientId, Type, StartDate, EndDate FROM [{sourceDb}].dbo.Memberships;
                SET IDENTITY_INSERT [{targetDb}].dbo.Memberships OFF;

                SET IDENTITY_INSERT [{targetDb}].dbo.Payments ON;
                INSERT INTO [{targetDb}].dbo.Payments (Id, ClientId, Amount, Date, Type)
                SELECT Id, ClientId, Amount, Date, Type FROM [{sourceDb}].dbo.Payments;
                SET IDENTITY_INSERT [{targetDb}].dbo.Payments OFF;
            ";

            cmd.ExecuteNonQuery();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [SYNC] {source} -> {target} завершена");
        }

        public static void ReplicateIfPrimary()
        {
            if (ActiveRole != InstanceRole.Primary) return;
            if (!IsAvailable(InstanceRole.Replica)) return;

            try
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Replication -- START");
                SyncPrimaryToReplica();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Replication -- SUCCESS");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Replication -- FAIL: {ex.Message}");
            }
        }

        public static void Initialize(bool recreate = false)
        {
            lock (_dbLock) // Защита от параллельных тестов
            {
                InitializeInstance(InstanceRole.Primary, recreate);
                InitializeInstance(InstanceRole.Replica, recreate);
            }
        }

        private static void InitializeInstance(InstanceRole role, bool recreate)
        {
            var connStr = GetConnectionString(role);
            var masterConnStr = GetMasterConnectionString(connStr);
            var dbName = new SqlConnectionStringBuilder(connStr).InitialCatalog;

            if (recreate)
            {
                using var master = new SqlConnection(masterConnStr);
                master.Open();
                using var drop = master.CreateCommand();
                drop.CommandText = $@"
                    IF DB_ID('{dbName}') IS NOT NULL
                    BEGIN
                        ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                        DROP DATABASE [{dbName}];
                    END";
                drop.ExecuteNonQuery();
            }

            using (var master = new SqlConnection(masterConnStr))
            {
                master.Open();
                using var createDb = master.CreateCommand();
                createDb.CommandText = $"IF DB_ID('{dbName}') IS NULL CREATE DATABASE [{dbName}];";
                createDb.ExecuteNonQuery();
            }

            using var conn = new SqlConnection(connStr);
            conn.Open();
            using var createTables = conn.CreateCommand();
            createTables.CommandText = @"
                IF OBJECT_ID('Trainers','U') IS NULL
                CREATE TABLE Trainers (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    Name NVARCHAR(100) NOT NULL,
                    Specialization NVARCHAR(100) NOT NULL
                );

                IF OBJECT_ID('Clients','U') IS NULL
                CREATE TABLE Clients (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    Name NVARCHAR(100) NOT NULL,
                    Email NVARCHAR(100) NOT NULL,
                    Phone NVARCHAR(50) NOT NULL
                );

                IF OBJECT_ID('TrainingSlots','U') IS NULL
                CREATE TABLE TrainingSlots (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    TrainerId INT NOT NULL,
                    DateTime DATETIME2 NOT NULL,
                    AvailablePlaces INT NOT NULL CHECK (AvailablePlaces >= 0),
                    CONSTRAINT FK_Slots_Trainers FOREIGN KEY (TrainerId) REFERENCES Trainers(Id)
                );

                IF OBJECT_ID('Trainings','U') IS NULL
                CREATE TABLE Trainings (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    ClientId INT NOT NULL,
                    TrainerId INT NOT NULL,
                    DateTime DATETIME2 NOT NULL,
                    Duration INT NOT NULL,
                    Status NVARCHAR(50) NOT NULL,
                    CONSTRAINT FK_Trainings_Clients FOREIGN KEY (ClientId) REFERENCES Clients(Id),
                    CONSTRAINT FK_Trainings_Trainers FOREIGN KEY (TrainerId) REFERENCES Trainers(Id)
                );

                IF OBJECT_ID('Memberships','U') IS NULL
                CREATE TABLE Memberships (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    ClientId INT NOT NULL,
                    Type NVARCHAR(50) NOT NULL,
                    StartDate DATETIME2 NOT NULL,
                    EndDate DATETIME2 NOT NULL,
                    CONSTRAINT FK_Memberships_Clients FOREIGN KEY (ClientId) REFERENCES Clients(Id)
                );

                IF OBJECT_ID('Payments','U') IS NULL
                CREATE TABLE Payments (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    ClientId INT NOT NULL,
                    Amount DECIMAL(18,2) NOT NULL,
                    Date DATETIME2 NOT NULL,
                    Type NVARCHAR(50) NOT NULL,
                    CONSTRAINT FK_Payments_Clients FOREIGN KEY (ClientId) REFERENCES Clients(Id)
                );
            ";
            createTables.ExecuteNonQuery();
        }

        public static void Seed()
        {
            lock (_dbLock) // Защита от параллельных тестов
            {
                SeedInstance(InstanceRole.Primary);
                try { SyncPrimaryToReplica(); } catch { }
            }
        }

        private static void SeedInstance(InstanceRole role)
        {
            // ПРИНУДИТЕЛЬНАЯ ОЧИСТКА: Гарантирует, что ID тренеров начнутся с 1
            ResetInstance(role);

            using var conn = new SqlConnection(GetConnectionString(role));
            conn.Open();

            InsertTrainer(conn, "Алексей", "Йога");
            InsertTrainer(conn, "Мария", "Фитнес");

            InsertClient(conn, "Иван", "ivan@test.ru", "+375292223344");
            InsertClient(conn, "Петр", "petr@test.ru", "+375292333445");

            // Слот 1: 1 место — используется тестами ЛР4/ЛР5 (race condition, lock, semaphore).
            AddSlot(conn, 1, new DateTime(2030, 1, 1, 18, 0, 0), 1);

            // Дополнительные слоты для демонстрации ЛР6 (Pub/Sub)
            AddSlot(conn, 1, new DateTime(2030, 1, 2, 18, 0, 0), 5);
            AddSlot(conn, 1, new DateTime(2030, 1, 3, 18, 0, 0), 5);
            AddSlot(conn, 1, new DateTime(2030, 1, 4, 18, 0, 0), 5);
            AddSlot(conn, 2, new DateTime(2030, 1, 2, 19, 0, 0), 5);
            AddSlot(conn, 2, new DateTime(2030, 1, 3, 19, 0, 0), 5);
        }

        public static void Reset()
        {
            lock (_dbLock) // Защита от параллельных тестов
            {
                ResetInstance(InstanceRole.Primary);
                ResetInstance(InstanceRole.Replica);
            }
        }

        private static void ResetInstance(InstanceRole role)
        {
            using var conn = new SqlConnection(GetConnectionString(role));
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM Payments;
                DELETE FROM Memberships;
                DELETE FROM Trainings;
                DELETE FROM TrainingSlots;
                DELETE FROM Clients;
                DELETE FROM Trainers;

                DBCC CHECKIDENT('Payments', RESEED, 0) WITH NO_INFOMSGS;
                DBCC CHECKIDENT('Memberships', RESEED, 0) WITH NO_INFOMSGS;
                DBCC CHECKIDENT('Trainings', RESEED, 0) WITH NO_INFOMSGS;
                DBCC CHECKIDENT('TrainingSlots', RESEED, 0) WITH NO_INFOMSGS;
                DBCC CHECKIDENT('Clients', RESEED, 0) WITH NO_INFOMSGS;
                DBCC CHECKIDENT('Trainers', RESEED, 0) WITH NO_INFOMSGS;
            ";
            cmd.ExecuteNonQuery();
        }

        private static void InsertTrainer(SqlConnection conn, string name, string specialization)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Trainers (Name, Specialization) VALUES (@n, @s);";
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@s", specialization);
            cmd.ExecuteNonQuery();
        }

        private static void InsertClient(SqlConnection conn, string name, string email, string phone)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Clients (Name, Email, Phone) VALUES (@n, @e, @p);";
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@e", email);
            cmd.Parameters.AddWithValue("@p", phone);
            cmd.ExecuteNonQuery();
        }

        private static void AddSlot(SqlConnection conn, int trainerId, DateTime dt, int places)
        {
            using var slot = conn.CreateCommand();
            slot.CommandText = @"INSERT INTO TrainingSlots (TrainerId, DateTime, AvailablePlaces)
                                 VALUES (@t, @d, @p);";
            slot.Parameters.AddWithValue("@t", trainerId);
            slot.Parameters.AddWithValue("@d", dt);
            slot.Parameters.AddWithValue("@p", places);
            slot.ExecuteNonQuery();
        }

        public static int GetAvailablePlaces(int slotId)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT AvailablePlaces FROM TrainingSlots WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", slotId);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? -1 : Convert.ToInt32(result);
        }

        public static int CountTrainings()
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Trainings;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public static int GetSlotId(int trainerId, DateTime dt)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id FROM TrainingSlots WHERE TrainerId=@t AND DateTime=@d;";
            cmd.Parameters.AddWithValue("@t", trainerId);
            cmd.Parameters.AddWithValue("@d", dt);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? -1 : Convert.ToInt32(result);
        }

        public static int GetAvailablePlacesFrom(InstanceRole role, int slotId)
        {
            using var conn = OpenConnection(role);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT AvailablePlaces FROM TrainingSlots WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", slotId);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? -1 : Convert.ToInt32(result);
        }
    }
}