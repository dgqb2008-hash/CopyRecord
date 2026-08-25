using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CopyRecord
{
    internal sealed class SqliteDatabase : IDisposable
    {
        private const int Ok = 0;
        private const int Row = 100;
        private const int Done = 101;
        private const int OpenReadWrite = 0x00000002;
        private const int OpenCreate = 0x00000004;
        private const int OpenFullMutex = 0x00010000;
        private static readonly IntPtr Transient = new IntPtr(-1);
        private IntPtr _database;

        internal SqliteDatabase(string path)
        {
            int result = sqlite3_open_v2(ToUtf8(path), out _database,
                OpenReadWrite | OpenCreate | OpenFullMutex, IntPtr.Zero);
            if (result != Ok) throw CreateException("无法打开数据库", result);
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA synchronous=NORMAL;");
            Execute("PRAGMA foreign_keys=ON;");
            Execute("PRAGMA busy_timeout=3000;");
        }

        internal void Execute(string sql, params object[] values)
        {
            using (Statement statement = Prepare(sql, values))
            {
                int result = sqlite3_step(statement.Handle);
                if (result != Done && result != Row) throw CreateException("数据库写入失败", result);
            }
        }

        internal List<T> Query<T>(string sql, Func<IntPtr, T> projector, params object[] values)
        {
            List<T> rows = new List<T>();
            using (Statement statement = Prepare(sql, values))
            {
                while (true)
                {
                    int result = sqlite3_step(statement.Handle);
                    if (result == Done) break;
                    if (result != Row) throw CreateException("数据库查询失败", result);
                    rows.Add(projector(statement.Handle));
                }
            }
            return rows;
        }

        internal object Scalar(string sql, params object[] values)
        {
            using (Statement statement = Prepare(sql, values))
            {
                int result = sqlite3_step(statement.Handle);
                if (result == Done) return null;
                if (result != Row) throw CreateException("数据库查询失败", result);
                int type = sqlite3_column_type(statement.Handle, 0);
                if (type == 1) return sqlite3_column_int64(statement.Handle, 0);
                if (type == 3) return ColumnText(statement.Handle, 0);
                return null;
            }
        }

        internal void Transaction(Action action)
        {
            Execute("BEGIN IMMEDIATE;");
            try
            {
                action();
                Execute("COMMIT;");
            }
            catch
            {
                try { Execute("ROLLBACK;"); }
                catch { }
                throw;
            }
        }

        internal static string ColumnText(IntPtr statement, int index)
        {
            IntPtr pointer = sqlite3_column_text(statement, index);
            if (pointer == IntPtr.Zero) return null;
            int length = sqlite3_column_bytes(statement, index);
            if (length <= 0) return string.Empty;
            byte[] bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        internal static int ColumnInt(IntPtr statement, int index)
        {
            return sqlite3_column_int(statement, index);
        }

        internal static long ColumnInt64(IntPtr statement, int index)
        {
            return sqlite3_column_int64(statement, index);
        }

        private Statement Prepare(string sql, object[] values)
        {
            IntPtr statement;
            int result = sqlite3_prepare_v2(_database, ToUtf8(sql), -1, out statement, IntPtr.Zero);
            if (result != Ok) throw CreateException("数据库语句无效", result);
            Statement prepared = new Statement(statement);
            try
            {
                for (int index = 0; index < values.Length; index++) Bind(statement, index + 1, values[index]);
                return prepared;
            }
            catch
            {
                prepared.Dispose();
                throw;
            }
        }

        private void Bind(IntPtr statement, int index, object value)
        {
            int result;
            if (value == null)
            {
                result = sqlite3_bind_null(statement, index);
            }
            else if (value is bool)
            {
                result = sqlite3_bind_int(statement, index, (bool)value ? 1 : 0);
            }
            else if (value is int)
            {
                result = sqlite3_bind_int(statement, index, (int)value);
            }
            else if (value is long)
            {
                result = sqlite3_bind_int64(statement, index, (long)value);
            }
            else
            {
                byte[] bytes = ToUtf8(Convert.ToString(value));
                result = sqlite3_bind_text(statement, index, bytes, bytes.Length - 1, Transient);
            }
            if (result != Ok) throw CreateException("数据库参数绑定失败", result);
        }

        private Exception CreateException(string prefix, int result)
        {
            string message = _database == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(sqlite3_errmsg(_database));
            return new InvalidOperationException(prefix + "（SQLite " + result + "）：" + message);
        }

        private static byte[] ToUtf8(string value)
        {
            return System.Text.Encoding.UTF8.GetBytes((value ?? string.Empty) + "\0");
        }

        public void Dispose()
        {
            if (_database == IntPtr.Zero) return;
            sqlite3_close_v2(_database);
            _database = IntPtr.Zero;
        }

        private sealed class Statement : IDisposable
        {
            internal IntPtr Handle { get; private set; }
            internal Statement(IntPtr handle) { Handle = handle; }
            public void Dispose()
            {
                if (Handle == IntPtr.Zero) return;
                sqlite3_finalize(Handle);
                Handle = IntPtr.Zero;
            }
        }

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(byte[] filename, out IntPtr database, int flags, IntPtr virtualFileSystem);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close_v2(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr database, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_null(IntPtr statement, int index);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_int(IntPtr statement, int index, int value);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int bytes, IntPtr destructor);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_type(IntPtr statement, int index);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_bytes(IntPtr statement, int index);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_int(IntPtr statement, int index);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr statement, int index);
    }
}
