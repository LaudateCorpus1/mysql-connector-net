﻿// Copyright (c) 2013, 2021, Oracle and/or its affiliates.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License, version 2.0, as
// published by the Free Software Foundation.
//
// This program is also distributed with certain software (including
// but not limited to OpenSSL) that is licensed under separate terms,
// as designated in a particular file or component or in included license
// documentation.  The authors of MySQL hereby grant you an
// additional permission to link the program and your derivative works
// with the separately licensed software that they have included with
// MySQL.
//
// Without limiting anything contained in the foregoing, this file,
// which is part of MySQL Connector/NET, is also subject to the
// Universal FOSS Exception, version 1.0, a copy of which can be found at
// http://oss.oracle.com/licenses/universal-foss-exception.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License, version 2.0, for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software Foundation, Inc.,
// 51 Franklin St, Fifth Floor, Boston, MA 02110-1301  USA

using System;
using System.Data;
using System.Diagnostics;
using System.Security.Authentication;
using System.Threading.Tasks;
using MySql.Data.Common;
using NUnit.Framework;

namespace MySql.Data.MySqlClient.Tests
{
  public class ConnectionTests : TestBase
  {
    const string _EXPIRED_USER = "expireduser";
    const string _EXPIRED_HOST = "localhost";

    [Test]
    public void TestConnectionStrings()
    {
      MySqlConnection c = new MySqlConnection();

      // public properties
      Assert.True(15 == c.ConnectionTimeout, "ConnectionTimeout");
      Assert.True(String.Empty == c.Database, "Database");
      Assert.True(String.Empty == c.DataSource, "DataSource");
      Assert.True(false == c.UseCompression, "Use Compression");
      Assert.True(ConnectionState.Closed == c.State, "State");

      c = new MySqlConnection("connection timeout=25; user id=myuser; " +
          "password=mypass; database=Test;server=myserver; use compression=true; " +
          "pooling=false;min pool size=5; max pool size=101");

      // public properties
      Assert.True(25 == c.ConnectionTimeout, "ConnectionTimeout");
      Assert.True("Test" == c.Database, "Database");
      Assert.True("myserver" == c.DataSource, "DataSource");
      Assert.True(true == c.UseCompression, "Use Compression");
      Assert.True(ConnectionState.Closed == c.State, "State");

      c.ConnectionString = "connection timeout=15; user id=newuser; " +
          "password=newpass; port=3308; database=mydb; data source=myserver2; " +
          "use compression=true; pooling=true; min pool size=3; max pool size=76";

      // public properties
      Assert.True(15 == c.ConnectionTimeout, "ConnectionTimeout");
      Assert.True("mydb" == c.Database, "Database");
      Assert.True("myserver2" == c.DataSource, "DataSource");
      Assert.True(true == c.UseCompression, "Use Compression");
      Assert.True(ConnectionState.Closed == c.State, "State");

      // Bug #30791289 - MYSQLCONNECTION(NULL) NOW THROWS NULLREFERENCEEXCEPTION
      var conn = new MySqlConnection("server=localhost;");
      conn.ConnectionString = null;
      Assert.AreEqual(string.Empty, conn.ConnectionString);
    }

    [Test]
    public void ChangeDatabase()
    {
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true));
      using (c)
      {
        c.Open();
        Assert.True(c.State == ConnectionState.Open);
        Assert.AreEqual(connStr.Database, c.Database);

        string dbName = CreateDatabase("db1");
        c.ChangeDatabase(dbName);
        Assert.AreEqual(dbName, c.Database);
      }
    }

    [Test]
    public void ConnectingAsUTF8()
    {
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      connStr.CharacterSet = "utf8";
      using (MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true)))
      {
        c.Open();

        MySqlCommand cmd = new MySqlCommand(
            "CREATE TABLE test (id varbinary(16), active bit) CHARACTER SET utf8", c);
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO test (id, active) VALUES (CAST(0x1234567890 AS Binary), true)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO test (id, active) VALUES (CAST(0x123456789a AS Binary), true)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO test (id, active) VALUES (CAST(0x123456789b AS Binary), true)";
        cmd.ExecuteNonQuery();
      }

      using (MySqlConnection d = new MySqlConnection(connStr.GetConnectionString(true)))
      {
        d.Open();

        MySqlCommand cmd2 = new MySqlCommand("SELECT id, active FROM test", d);
        using (MySqlDataReader reader = cmd2.ExecuteReader())
        {
          Assert.True(reader.Read());
          Assert.True(reader.GetBoolean(1));
        }
      }
    }

    /// <summary>
    /// Bug #13658 connection.state does not update on Ping()
    /// </summary>
    [Test]
    public void PingUpdatesState()
    {
      var conn2 = GetConnection();
      KillConnection(conn2);
      Assert.False(conn2.Ping());
      Assert.True(conn2.State == ConnectionState.Closed);
      conn2.Open();
      conn2.Close();
    }

    /// <summary>
    /// Bug #16659  	Can't use double quotation marks(") as password access server by Connector/NET
    /// </summary>
    [Test]
    [Ignore("Fix for 8.0.5")]
    public void ConnectWithQuotePassword()
    {
      ExecuteSQL("GRANT ALL ON *.* to 'quotedUser'@'%' IDENTIFIED BY '\"'", true);
      ExecuteSQL("GRANT ALL ON *.* to 'quotedUser'@'localhost' IDENTIFIED BY '\"'", true);
      MySqlConnectionStringBuilder settings = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      settings.UserID = "quotedUser";
      settings.Password = "\"";
      using (MySqlConnection c = new MySqlConnection(Connection.ConnectionString))
      {
        c.Open();
      }
    }

    /// <summary>
    /// Bug #24802 Error Handling 
    /// </summary>
    [Test]
    public void TestConnectingSocketBadHostName()
    {
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      connStr.Server = "foobar";
      MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true));
      var ex = Assert.Throws<MySqlException>(() => c.Open());
      Assert.AreEqual((int)MySqlErrorCode.UnableToConnectToHost, ex.Number);
    }

    /// <summary>
    /// Bug #29123  	Connection String grows with each use resulting in OutOfMemoryException
    /// </summary>
    [Test]
    public void ConnectionStringNotAffectedByChangeDatabase()
    {
      for (int i = 0; i < 10; i++)
      {
        string connStr = Connection.ConnectionString + ";pooling=false";
        connStr = connStr.Replace("database", "Initial Catalog");
        connStr = connStr.Replace("persist security info=true",
            "persist security info=false");
        using (MySqlConnection c = new MySqlConnection(connStr))
        {
          c.Open();
          string str = c.ConnectionString;
          int index = str.IndexOf("Database=");
          Assert.AreEqual(-1, index);
        }
      }
    }

    [Test]
    [Ignore("dotnet core seems to keep objects alive")] // reference https://github.com/dotnet/coreclr/issues/13490
    public void ConnectionCloseByGC()
    {
      int threadId;
      ConnectionClosedCheck check = new ConnectionClosedCheck();

      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      connStr.Pooling = false;
      MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true));
      c.StateChange += new StateChangeEventHandler(check.stateChangeHandler);
      c.Open();
      threadId = c.ServerThread;
      WeakReference wr = new WeakReference(c);
      Assert.True(wr.IsAlive);
      c = null;
      GC.Collect();
      GC.WaitForPendingFinalizers();
      Assert.False(wr.IsAlive);
      Assert.True(check.closed);

      MySqlCommand cmd = new MySqlCommand("KILL " + threadId, Connection);
      cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Bug #31262 NullReferenceException in MySql.Data.MySqlClient.NativeDriver.ExecuteCommand
    /// </summary>
    [Test]
    public void ConnectionNotOpenThrowningBadException()
    {
      var c2 = new MySqlConnection(Connection.ConnectionString);
      MySqlCommand command = new MySqlCommand();
      command.Connection = c2;

      MySqlCommand cmdCreateTable = new MySqlCommand("DROP TABLE IF EXISTS `test`.`contents_catalog`", c2);
      cmdCreateTable.CommandType = CommandType.Text;
      cmdCreateTable.CommandTimeout = 0;
      Assert.Throws<InvalidOperationException>(() => cmdCreateTable.ExecuteNonQuery());
    }

    /// <summary>
    /// Bug #35619 creating a MySql connection from toolbox generates an error
    /// </summary>
    [Test]
    public void NullConnectionString()
    {
      MySqlConnection c = new MySqlConnection();
      c.ConnectionString = null;
    }

    /// <summary>
    /// Bug #53097  	Connection.Ping() closes connection if executed on a connection with datareader
    /// </summary>
    [Test]
    public void PingWhileReading()
    {
      using (MySqlConnection conn = new MySqlConnection(Connection.ConnectionString))
      {
        conn.Open();
        MySqlCommand command = new MySqlCommand("SELECT 1", conn);

        using (MySqlDataReader reader = command.ExecuteReader())
        {
          reader.Read();
          Assert.Throws<MySqlException>(() => conn.Ping());
        }
      }
    }

#if NET452
    /// <summary>
    /// Test if keepalive parameters work.
    /// </summary>
    [Test]
    public void Keepalive()
    {
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      connStr.Keepalive = 1;
      using (MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true)))
      {
        c.Open();
      }
    }
#endif

    #region Async

    [Test]
    public async Task TransactionAsync()
    {
      ExecuteSQL("Create Table TranAsyncTest(key2 varchar(50), name varchar(50), name2 varchar(50))");
      ExecuteSQL("INSERT INTO TranAsyncTest VALUES('P', 'Test1', 'Test2')");

      MySqlTransaction txn = await Connection.BeginTransactionAsync();
      MySqlConnection c = txn.Connection;
      Assert.AreEqual(Connection, c);
      MySqlCommand cmd = new MySqlCommand("SELECT name, name2 FROM TranAsyncTest WHERE key2='P'", Connection, txn);
      MySqlTransaction t2 = cmd.Transaction;
      Assert.AreEqual(txn, t2);
      MySqlDataReader reader = null;
      try
      {
        reader = cmd.ExecuteReader();
        reader.Close();
        txn.Commit();
      }
      catch (Exception ex)
      {
        Assert.False(ex.Message != string.Empty, ex.Message);
        txn.Rollback();
      }
      finally
      {
        if (reader != null) reader.Close();
      }
    }

    [Test]
    public async Task ChangeDataBaseAsync()
    {
      string dbName = CreateDatabase("db2");
      ExecuteSQL(String.Format(
        "CREATE TABLE `{0}`.`footest` (id INT NOT NULL, name VARCHAR(100), dt DATETIME, tm TIME,  `multi word` int, PRIMARY KEY(id))", dbName), true);

      await Connection.ChangeDataBaseAsync(dbName);

      var cmd = Connection.CreateCommand();
      cmd.CommandText = "SELECT COUNT(*) FROM footest";
      var count = cmd.ExecuteScalar();
    }

    [Test]
    public async Task OpenAndCloseConnectionAsync()
    {
      var conn = new MySqlConnection(Connection.ConnectionString);
      await conn.OpenAsync();
      Assert.True(conn.State == ConnectionState.Open);
      await conn.CloseAsync();
      Assert.True(conn.State == ConnectionState.Closed);
    }

    [Test]
    public async Task ClearPoolAsync()
    {
      MySqlConnection c1 = new MySqlConnection(Connection.ConnectionString);
      MySqlConnection c2 = new MySqlConnection(Connection.ConnectionString);
      c1.Open();
      c2.Open();
      c1.Close();
      c2.Close();
      await c1.ClearPoolAsync(c1);
      await c2.ClearPoolAsync(c1);
    }

    [Test]
    public async Task ClearAllPoolsAsync()
    {
      MySqlConnection c1 = new MySqlConnection(Connection.ConnectionString);
      MySqlConnection c2 = new MySqlConnection(Connection.ConnectionString);
      c1.Open();
      c2.Open();
      c1.Close();
      c2.Close();
      await c1.ClearAllPoolsAsync();
      await c2.ClearAllPoolsAsync();
    }

    [Test]
    public async Task GetSchemaCollectionAsync()
    {
      var schemaColl = await Connection.GetSchemaCollectionAsync("MetaDataCollections", null);
      Assert.NotNull(schemaColl);
    }

    #endregion

    #region Connection Attributes/Options

    [Test]
    [Property("Category", "Security")]
    public void TestConnectingSocketBadUserName()
    {
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      connStr.UserID = "bad_one";
      MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true));
      Assert.Throws<MySqlException>(() => c.Open());
    }

    [Test]
    [Property("Category", "Security")]
    public void TestConnectingSocketBadDbName()
    {
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      connStr.Password = "bad_pwd";
      MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true));
      Assert.Throws<MySqlException>(() => c.Open());
    }

    [Test]
    [Property("Category", "Security")]
    public void TestPersistSecurityInfoCachingPasswords()
    {
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);

      // Persist Security Info = true means that it should be returned
      connStr.PersistSecurityInfo = true;
      MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true));
      c.Open();
      c.Close();
      MySqlConnectionStringBuilder afterOpenSettings = new MySqlConnectionStringBuilder(c.ConnectionString);
      Assert.AreEqual(connStr.Password, afterOpenSettings.Password);

      // Persist Security Info = false means that it should not be returned
      connStr.PersistSecurityInfo = false;
      c = new MySqlConnection(connStr.GetConnectionString(true));
      c.Open();
      c.Close();
      afterOpenSettings = new MySqlConnectionStringBuilder(c.ConnectionString);
      Assert.True(String.IsNullOrEmpty(afterOpenSettings.Password));
    }

    /// <summary>
    /// Bug #30502718  MYSQLCONNECTION.CLONE DISCLOSES CONNECTION PASSWORD
    /// </summary>
    [Test]
    [Property("Bug", "30502718")]
    public void CloneConnectionDisclosePassword()
    {
      // Verify original connection doesn't show password before and after open connection
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      connStr.PersistSecurityInfo = false;
      MySqlConnection c = new MySqlConnection(connStr.ConnectionString);

      // The password, is not returned as part of the connection if the connection is open or has ever been in an open state
      StringAssert.Contains("password", c.ConnectionString);

      // After open password should not be displayed
      c.Open();
      StringAssert.DoesNotContain("password", c.ConnectionString);

      // Verify clone from open connection should not show password
      var cloneConnection = (MySqlConnection)c.Clone();
      StringAssert.DoesNotContain("password", cloneConnection.ConnectionString);

      // After close connection the password should not be displayed
      c.Close();
      StringAssert.DoesNotContain("password", c.ConnectionString);

      // Verify clone connection doesn't show password after open connection
      cloneConnection.Open();
      StringAssert.DoesNotContain("password", cloneConnection.ConnectionString);

      // Verify clone connection doesn't show password after close connection
      cloneConnection.Close();
      StringAssert.DoesNotContain("password", cloneConnection.ConnectionString);

      // Verify password for a clone of closed connection, password should appears
      var closedConnection = new MySqlConnection(connStr.ConnectionString);
      var cloneClosed = (MySqlConnection)closedConnection.Clone();
      StringAssert.Contains("password", cloneClosed.ConnectionString);

      // Open connection of a closed connection clone, password should be empty
      Assert.False(cloneClosed.hasBeenOpen);
      cloneClosed.Open();
      StringAssert.DoesNotContain("password", cloneClosed.ConnectionString);
      Assert.True(cloneClosed.hasBeenOpen);

      // Close connection of a closed connection clone, password should be empty
      cloneClosed.Close();
      StringAssert.DoesNotContain("password", cloneClosed.ConnectionString);

      // Clone Password shloud be present if PersistSecurityInfo is true
      connStr.PersistSecurityInfo = true;
      c = new MySqlConnection(connStr.ConnectionString);
      cloneConnection = (MySqlConnection)c.Clone();
      StringAssert.Contains("password", cloneConnection.ConnectionString);
    }

    [Test]
    [Property("Category", "Security")]
    public void ConnectionTimeout()
    {
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      connStr.Port = 3000;
      connStr.ConnectionTimeout = 5;
      MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true));

      DateTime start = DateTime.Now;
      Assert.Throws<MySqlException>(() => c.Open());
      TimeSpan diff = DateTime.Now.Subtract(start);
      Assert.True(diff.TotalSeconds < 10, $"Timeout exceeded: {diff.TotalSeconds}");
    }

    [Test]
    [Ignore("Fix for 8.0.5")]
    [Property("Category", "Security")]
    public void ConnectInVariousWays()
    {
      // connect with no db
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      connStr.Database = null;
      MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true));
      c.Open();
      c.Close();

      ExecuteSQL("GRANT ALL ON *.* to 'nopass'@'%'", true);
      ExecuteSQL("GRANT ALL ON *.* to 'nopass'@'localhost'", true);
      ExecuteSQL("FLUSH PRIVILEGES", true);

      // connect with no password
      connStr.UserID = "nopass";
      connStr.Password = null;
      c = new MySqlConnection(connStr.GetConnectionString(true));
      c.Open();
      c.Close();

      connStr.Password = "";
      c = new MySqlConnection(connStr.GetConnectionString(true));
      c.Open();
      c.Close();
    }

    /// <summary>
    /// Bug #10281 Clone issue with MySqlConnection
    /// Bug #27269 MySqlConnection.Clone does not mimic SqlConnection.Clone behaviour
    /// </summary>
    [Test]
    [Property("Category", "Security")]
    public void TestConnectionCloneRetainsPassword()
    {
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      connStr.PersistSecurityInfo = false;

      MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true));
      c.Open();
      c.Close();
      MySqlConnection clone = (MySqlConnection)c.Clone();
      clone.Open();
      clone.Close();
    }

    /// <summary>
    /// Bug #13321 Persist security info does not woek
    /// </summary>
    [Test]
    [Property("Category", "Security")]
    public void PersistSecurityInfo()
    {
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      connStr.PersistSecurityInfo = false;

      Assert.False(String.IsNullOrEmpty(connStr.Password));
      MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true));
      c.Open();
      c.Close();
      connStr = new MySqlConnectionStringBuilder(c.ConnectionString);
      Assert.True(String.IsNullOrEmpty(connStr.Password));
    }

    /// <summary>
    /// Bug #31433 Username incorrectly cached for logon where case sensitive
    /// </summary>
    [Test]
    [Property("Category", "Security")]
    public void CaseSensitiveUserId()
    {
      MySqlConnectionStringBuilder connStr = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      string original_uid = connStr.UserID;
      connStr.UserID = connStr.UserID.ToUpper();
      MySqlConnection c = new MySqlConnection(connStr.GetConnectionString(true));
      Assert.Throws<MySqlException>(() => c.Open());

      connStr.UserID = original_uid;
      c = new MySqlConnection(connStr.GetConnectionString(true));
      c.Open();
      c.Close();
    }

    [Test]
    [Property("Category", "Security")]
    public void CanOpenConnectionAfterAborting()
    {
      MySqlConnection connection = new MySqlConnection(Connection.ConnectionString);
      connection.Open();
      Assert.AreEqual(ConnectionState.Open, connection.State);

      connection.Abort();
      Assert.AreEqual(ConnectionState.Closed, connection.State);

      connection.Open();
      Assert.AreEqual(ConnectionState.Open, connection.State);

      connection.Close();
    }

    /// <summary>
    /// Test for Connect attributes feature used in MySql Server > 5.6.6
    /// (Stores client connection data on server)
    /// </summary>
    [Test]
    [Property("Category", "Security")]
    public void ConnectAttributes()
    {
      if (Version < new Version(5, 6, 6)) return;
      if (!Connection.driver.SupportsConnectAttrs) return;

      MySqlCommand cmd = new MySqlCommand("SELECT * FROM performance_schema.session_connect_attrs WHERE PROCESSLIST_ID = connection_id()", Connection);
      MySqlDataReader dr = cmd.ExecuteReader();
      Assert.True(dr.HasRows, "No session_connect_attrs found");
      MySqlConnectAttrs connectAttrs = new MySqlConnectAttrs();
      bool isValidated = false;
      using (dr)
      {
        while (dr.Read())
        {
          if (dr.GetString(1).ToLowerInvariant().Contains("_client_name"))
          {
            Assert.AreEqual(connectAttrs.ClientName, dr.GetString(2));
            isValidated = true;
            break;
          }
        }
      }
      Assert.True(isValidated, "Missing _client_name attribute");
    }

    /// <summary>
    /// Test for password expiration feature in MySql Server 5.6 or higher
    /// </summary>
    [Test]
    [Property("Category", "Security")]
    public void PasswordExpiration()
    {
      if ((Version < new Version(5, 6, 6)) || (Version >= new Version(8, 0, 17))) return;

      string expiredfull = string.Format("'{0}'@'{1}'", _EXPIRED_USER, _EXPIRED_HOST);

      using (MySqlConnection conn = new MySqlConnection(Settings.ToString()))
      {
        MySqlCommand cmd = new MySqlCommand("", conn);

        // creates expired user
        SetupExpiredPasswordUser();

        // validates expired user
        var cnstrBuilder = new MySqlConnectionStringBuilder(Root.ConnectionString);
        cnstrBuilder.UserID = _EXPIRED_USER;
        cnstrBuilder.Password = _EXPIRED_USER + "1";
        conn.ConnectionString = cnstrBuilder.ConnectionString;
        conn.Open();

        cmd.CommandText = "SELECT 1";
        MySqlException ex = Assert.Throws<MySqlException>(() => cmd.ExecuteScalar());
        Assert.AreEqual(1820, ex.Number);

        if (Version >= new Version(5, 7, 6))
          cmd.CommandText = string.Format("SET PASSWORD = '{0}1'", _EXPIRED_USER);
        else
          cmd.CommandText = string.Format("SET PASSWORD = PASSWORD('{0}1')", _EXPIRED_USER);

        cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteScalar();
        conn.Close();
        conn.ConnectionString = Root.ConnectionString;
        conn.Open();
        MySqlHelper.ExecuteNonQuery(conn, String.Format("DROP USER " + expiredfull));
        conn.Close();
      }
    }

    [Test]
    [Property("Category", "Security")]
    public void TestNonSupportedOptions()
    {
      string connstr = Root.ConnectionString;
      connstr += ";CertificateFile=client.pfx;CertificatePassword=pass;SSL Mode=Required;";
      using (MySqlConnection c = new MySqlConnection(connstr))
      {
        c.Open();
        Assert.AreEqual(ConnectionState.Open, c.State);
      }
    }

    #endregion

    #region SSL

    [Test]
    [Property("Category", "Security")]
    public void SslPreferredByDefault()
    {
      MySqlCommand command = new MySqlCommand("SHOW SESSION STATUS LIKE 'Ssl_version';", Connection);
      using (MySqlDataReader reader = command.ExecuteReader())
      {
        Assert.True(reader.Read());
        StringAssert.StartsWith("TLSv1", reader.GetString(1));
      }
    }

    [Test]
    [Property("Category", "Security")]
    public void SslOverrided()
    {
      var cstrBuilder = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      cstrBuilder.SslMode = MySqlSslMode.None;
      cstrBuilder.AllowPublicKeyRetrieval = true;
      cstrBuilder.Database = "";
      using (MySqlConnection connection = new MySqlConnection(cstrBuilder.ConnectionString))
      {
        connection.Open();
        MySqlCommand command = new MySqlCommand("SHOW SESSION STATUS LIKE 'Ssl_version';", connection);
        using (MySqlDataReader reader = command.ExecuteReader())
        {
          Assert.True(reader.Read());
          Assert.AreEqual(string.Empty, reader.GetString(1));
        }
      }
    }

    /// <summary>
    /// A client can connect to MySQL server using SSL and a pfx file.
    /// <remarks>
    /// This test requires starting the server with SSL support.
    /// For instance, the following command line enables SSL in the server:
    /// mysqld --no-defaults --standalone --console --ssl-ca='MySQLServerDir'\mysql-test\std_data\cacert.pem --ssl-cert='MySQLServerDir'\mysql-test\std_data\server-cert.pem --ssl-key='MySQLServerDir'\mysql-test\std_data\server-key.pem
    /// </remarks>
    /// </summary>
    [Test]
    [Property("Category", "Security")]
    public void CanConnectUsingFileBasedCertificate()
    {
      string connstr = Connection.ConnectionString;
      connstr += ";CertificateFile=client.pfx;CertificatePassword=pass;SSL Mode=Required;";
      using (MySqlConnection c = new MySqlConnection(connstr))
      {
        c.Open();
        Assert.AreEqual(ConnectionState.Open, c.State);
        MySqlCommand command = new MySqlCommand("SHOW SESSION STATUS LIKE 'Ssl_version';", c);
        using (MySqlDataReader reader = command.ExecuteReader())
        {
          Assert.True(reader.Read());
          StringAssert.StartsWith("TLSv1", reader.GetString(1));
        }
      }
    }

    [TestCase("[]", null)]
    [TestCase("Tlsv1", "TLSv1")]
    [TestCase("Tlsv1.0, Tlsv1.1", "TLSv1.1")]
    [TestCase("Tlsv1.0, Tlsv1.1, Tlsv1.2", "TLSv1.2")]
    //#if NET48 || NETCOREAPP3_1 || NET5_0
    //    [TestCase("Tlsv1.3", "Tlsv1.3")]
    //    [TestCase("Tlsv1.0, Tlsv1.1, Tlsv1.2, Tlsv1.3", "Tlsv1.3")]
    //#endif
#if NET452
    [TestCase("Tlsv1.3", "")]
    [TestCase("Tlsv1.0, Tlsv1.1, Tlsv1.2, Tlsv1.3", "Tlsv1.2")]
#endif
    [Property("Category", "Security")]
    public void TlsVersionTest(string tlsVersion, string result)
    {
      var builder = new MySqlConnectionStringBuilder(Connection.ConnectionString);

      void SetTlsVersion() { builder.TlsVersion = tlsVersion; }
      if (result == null)
      {
        Assert.That(SetTlsVersion, Throws.Exception);
        return;
      }
      SetTlsVersion();
      var conn = new MySqlConnection(builder.ConnectionString);

      if (!String.IsNullOrWhiteSpace(result))
      {
        using (conn)
        {
          // TLSv1.0 and TLSv1.1 has been deprecated in Ubuntu 20.04 so an exception is thrown
          try { conn.Open(); }
          catch (Exception ex) { Assert.True(ex is AuthenticationException); return; }

          Assert.AreEqual(ConnectionState.Open, conn.State);
          MySqlCommand cmd = conn.CreateCommand();
          cmd.CommandText = "SHOW SESSION STATUS LIKE 'ssl_version'";
          using (MySqlDataReader dr = cmd.ExecuteReader())
          {
            Assert.True(dr.Read());
            StringAssert.AreEqualIgnoringCase(result, dr[1].ToString());
          }
        }
      }
      else
#if NET452
        Assert.Throws<NotSupportedException>(() => conn.Open());
#else
        Assert.Throws<System.ComponentModel.Win32Exception>(() => conn.Open());
#endif
    }

    [TestCase("Tlsv1.0", true)]
    [TestCase("Tlsv1.1", true)]
    [TestCase("Tlsv1.1, Tlsv1.0", true)]
    [TestCase("Tlsv1.2", false)]
    public void TlsDeprecatedVersions(string tlsVersion, bool shouldWarn)
    {
      var builder = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      builder.TlsVersion = tlsVersion;

      MySqlTrace.Listeners.Clear();
      MySqlTrace.Switch.Level = SourceLevels.Warning;
      GenericListener listener = new GenericListener();
      MySqlTrace.Listeners.Add(listener);
      int indexComma = builder.TlsVersion.IndexOf(',');
      tlsVersion = indexComma > 0 ? "Tls11" : builder.TlsVersion; //get the highest version of TLS (Tls11)

      using (var logConn = new MySqlConnection(builder.ConnectionString))
      {
        // TLSv1.0 and TLSv1.1 has been deprecated in Ubuntu 20.04 so an exception is thrown
        try { logConn.Open(); }
        catch (Exception ex) { Assert.True(ex is AuthenticationException); return; }

        if (shouldWarn)
          StringAssert.Contains(string.Format(Resources.TlsDeprecationWarning, tlsVersion), listener.Strings[0]);
        else
          Assert.IsEmpty(listener.Strings);
      }

      // Pooling
      listener.Clear();
      string pooling = builder.ConnectionString + ";Min Pool Size=10";
      MySqlConnection[] connArray = new MySqlConnection[10];
      for (int i = 0; i < connArray.Length; i++)
      {
        connArray[i] = new MySqlConnection(pooling);
        // TLSv1.0 and TLSv1.1 has been deprecated in Ubuntu 20.04 so an exception is thrown
        try { connArray[i].Open(); }
        catch (Exception ex) { Assert.True(ex is AuthenticationException); return; }

        if (shouldWarn)
          StringAssert.Contains(string.Format(Resources.TlsDeprecationWarning, tlsVersion), listener.Strings[i]);
        else
          Assert.IsEmpty(listener.Strings);
      }

      for (int i = 0; i < connArray.Length; i++)
      {
        KillConnection(connArray[i]);
        connArray[i].Close();
      }
    }

    #endregion

    [Test]
    public void IPv6Connection()
    {
      if (Version < new Version(5, 6, 0)) return;

      MySqlConnectionStringBuilder sb = new MySqlConnectionStringBuilder(Connection.ConnectionString);
      sb.Server = "::1";
      using (MySqlConnection conn = new MySqlConnection(sb.ToString()))
      {
        conn.Open();
        Assert.AreEqual(ConnectionState.Open, conn.State);
      }
    }

    private void SetupExpiredPasswordUser()
    {
      string expiredFull = $"'{_EXPIRED_USER}'@'{_EXPIRED_HOST}'";

      using (MySqlConnection conn = GetConnection(true))
      {
        MySqlCommand cmd = conn.CreateCommand();

        // creates expired user
        cmd.CommandText = $"SELECT COUNT(*) FROM mysql.user WHERE user='{_EXPIRED_USER}' AND host='{_EXPIRED_HOST}'";
        long count = (long)cmd.ExecuteScalar();
        if (count > 0)
          MySqlHelper.ExecuteNonQuery(conn, $"DROP USER {expiredFull}");

        MySqlHelper.ExecuteNonQuery(conn, $"CREATE USER {expiredFull} IDENTIFIED BY '{_EXPIRED_USER}1'");
        MySqlHelper.ExecuteNonQuery(conn, $"GRANT SELECT ON `{Settings.Database}`.* TO {expiredFull}");
        MySqlHelper.ExecuteNonQuery(conn, $"ALTER USER {expiredFull} PASSWORD EXPIRE");
      }
    }

    //[InlineData("SET NAMES 'latin1'")]
    [TestCase("SELECT VERSION()")]
    [TestCase("SHOW VARIABLES LIKE '%audit%'")]
    [Property("Category", "Security")]
    public void ExpiredPassword(string sql)
    {
      if (Version < new Version(8, 0, 18))
        return;

      MySqlConnectionStringBuilder sb = new MySqlConnectionStringBuilder(Settings.ConnectionString);
      sb.UserID = _EXPIRED_USER;
      sb.Password = _EXPIRED_USER + "1";
      SetupExpiredPasswordUser();
      using (MySqlConnection conn = new MySqlConnection(sb.ConnectionString))
      {
        conn.Open();
        Assert.AreEqual(ConnectionState.Open, conn.State);
        MySqlCommand cmd = new MySqlCommand(sql, conn);
        var ex = Assert.Throws<MySqlException>(() => cmd.ExecuteNonQuery());
        Assert.AreEqual(1820, ex.Number);
      }
    }

    [Test]
    [Property("Category", "Security")]
    public void ExpiredPwdWithOldPassword()
    {
      if ((Version < new Version(5, 6, 6)) || (Version >= new Version(8, 0, 17))) return;

      string expiredUser = _EXPIRED_USER;
      string expiredPwd = _EXPIRED_USER + 1;
      string newPwd = "newPwd";
      string host = Settings.Server;
      uint port = Settings.Port;

      SetupExpiredPasswordUser();

      var sb = new MySqlConnectionStringBuilder();
      sb.Server = host;
      sb.Port = port;
      sb.UserID = expiredUser;
      sb.Password = expiredPwd;
      using (MySqlConnection conn = new MySqlConnection(sb.ConnectionString))
      {
        conn.Open();
        string password = $"'{newPwd}'";
        if (Version < new Version(5, 7, 6))
          password = $"PASSWORD({password})";

        MySqlCommand cmd = new MySqlCommand($"SET PASSWORD FOR '{expiredUser}'@'{host}' = {password}", conn);
        cmd.ExecuteNonQuery();
      }

      sb.Password = newPwd;
      using (MySqlConnection conn = new MySqlConnection(sb.ConnectionString))
      {
        conn.Open();
        MySqlCommand cmd = new MySqlCommand("SELECT 8", conn);
        StringAssert.StartsWith("8", cmd.ExecuteScalar().ToString());
      }

      sb.Password = expiredPwd;
      using (MySqlConnection conn = new MySqlConnection(sb.ConnectionString))
      {
        Assert.Throws<MySqlException>(() => { conn.Open(); });
      }
    }

    /// <summary>
    /// Bug#32853205 - UNABLE TO CONNECT USING NAMED PIPES AND SHARED MEMORY
    /// To be able to connect using Named Pipes, it requires to start the server supporting the protocol
    /// mysqld --standalone --console --enable-named-pipe
    /// </summary>    
    [Test]
    [Ignore("To be able to connect using Named Pipes, it requires to start the server supporting the protocol")]
    public void ConnectUsingNamedPipes()
    {
      var sb = new MySqlConnectionStringBuilder()
      {
        Server = "localhost",
        Pooling = false,
        UserID = "root",
        ConnectionProtocol = MySqlConnectionProtocol.NamedPipe,
        SslMode = MySqlSslMode.Required
      };

      // Named Pipes connection protocol is not allowed to use SSL connections.
      using var conn = new MySqlConnection(sb.ConnectionString);
      var ex = Assert.Throws<MySqlException>(() => conn.Open());
      StringAssert.AreEqualIgnoringCase(string.Format(Resources.SslNotAllowedForConnectionProtocol, sb.ConnectionProtocol), ex.Message);
    }

    /// <summary>
    /// Bug#32853205 - UNABLE TO CONNECT USING NAMED PIPES AND SHARED MEMORY
    /// </summary>
    [Test]
    [Property("Category", "Security")]
    public void ConnectUsingSharedMemory()
    {
      if (!Platform.IsWindows()) Assert.Ignore("Shared Memory is only supported on Windows.");

      var sb = new MySqlConnectionStringBuilder()
      {
        Server = "localhost",
        Pooling = false,
        UserID = "root",
        ConnectionProtocol = MySqlConnectionProtocol.SharedMemory,
        SharedMemoryName = "MySQLSocket"
      };

      using (var conn = new MySqlConnection(sb.ConnectionString))
      {
        conn.Open();
        Assert.AreEqual(ConnectionState.Open, conn.State);
      }

      // Shared Memory connection protocol is not allowed to use SSL connections.
      sb.SslMode = MySqlSslMode.Required;
      using (var conn = new MySqlConnection(sb.ConnectionString))
      {
        var ex = Assert.Throws<MySqlException>(() => conn.Open());
        StringAssert.AreEqualIgnoringCase(string.Format(Resources.SslNotAllowedForConnectionProtocol, sb.ConnectionProtocol), ex.Message);
      }
    }

#if NET452
    /// <summary>
    ///  Fix for aborted connections MySQL bug 80997 OraBug 23346197
    /// </summary>
    [Test]
    public void MarkConnectionAsClosedProperlyWhenDisposing()
    {
      MySqlConnection con = new MySqlConnection(Connection.ConnectionString);
      con.Open();
      var cmd = new MySqlCommand("show global status like 'aborted_clients'", con);
      MySqlDataReader r = cmd.ExecuteReader();
      r.Read();
      int numClientsAborted = r.GetInt32(1);
      r.Close();

      AppDomain appDomain = FullTrustSandbox.CreateFullTrustDomain();
      FullTrustSandbox sandbox = (FullTrustSandbox)appDomain.CreateInstanceAndUnwrap(
          typeof(FullTrustSandbox).Assembly.FullName,
          typeof(FullTrustSandbox).FullName);
      try
      {
        MySqlConnection connection = sandbox.TryOpenConnection(Connection.ConnectionString);
        Assert.NotNull(connection);
        Assert.True(connection.State == ConnectionState.Open);
      }
      finally
      {
        AppDomain.Unload(appDomain);
      }

      r = cmd.ExecuteReader();
      r.Read();
      int numClientsAborted2 = r.GetInt32(1);
      r.Close();
      Assert.AreEqual(numClientsAborted, numClientsAborted);
      con.Close();
    }
#endif

    /*
    [Test]
    public void AnonymousLogin()
    {
      suExecSQL(String.Format("GRANT ALL ON *.* to ''@'{0}' IDENTIFIED BY 'set_to_blank'", host));
      suExecSQL("UPDATE mysql.user SET password='' WHERE password='set_to_blank'");

      MySqlConnection c = new MySqlConnection(String.Empty);
      c.Open();
      c.Close();
    }
    */

    //    /// <summary>
    //    /// Bug #30964 StateChange imperfection
    //    /// </summary>
    //    MySqlConnection rqConnection;


    //    [Test]
    //    public void RunningAQueryFromStateChangeHandler()
    //    {
    //      string connStr = st.GetConnectionString(true);
    //      using (rqConnection = new MySqlConnection(connStr))
    //      {
    //        rqConnection.StateChange += new StateChangeEventHandler(RunningQueryStateChangeHandler);
    //        rqConnection.Open();
    //      }
    //    }

    //    void RunningQueryStateChangeHandler(object sender, StateChangeEventArgs e)
    //    {
    //      if (e.CurrentState == ConnectionState.Open)
    //      {
    //        MySqlCommand cmd = new MySqlCommand("SELECT 1", rqConnection);
    //        object o = cmd.ExecuteScalar();
    //        Assert.AreEqual(1, Convert.ToInt32(o));
    //      }
    //    }

    //    [Test]
    //    public void CanOpenConnectionInMediumTrust()
    //    {
    //      AppDomain appDomain = PartialTrustSandbox.CreatePartialTrustDomain();

    //      PartialTrustSandbox sandbox = (PartialTrustSandbox)appDomain.CreateInstanceAndUnwrap(
    //          typeof(PartialTrustSandbox).Assembly.FullName,
    //          typeof(PartialTrustSandbox).FullName);

    //      try
    //      {
    //        MySqlConnection connection = sandbox.TryOpenConnection(st.GetConnectionString(true));
    //        Assert.True(null != connection);

    //        Assert.True(connection.State == ConnectionState.Open);
    //        connection.Close();

    //        //Now try with logging enabled
    //        connection = sandbox.TryOpenConnection(st.GetConnectionString(true) + ";logging=true");
    //        Assert.True(null != connection);
    //        Assert.True(connection.State == ConnectionState.Open);
    //        connection.Close();

    //        //Now try with Usage Advisor enabled
    //        connection = sandbox.TryOpenConnection(st.GetConnectionString(true) + ";Use Usage Advisor=true");
    //        Assert.True(null != connection);
    //        Assert.True(connection.State == ConnectionState.Open);
    //        connection.Close();
    //      }
    //      finally
    //      {
    //        AppDomain.Unload(appDomain);
    //      }
    //    }

    ///// <summary>
    ///// Fix for bug http://bugs.mysql.com/bug.php?id=63942 (Connections not closed properly when using pooling)
    ///// </summary>
    //[Test]
    //public void ReleasePooledConnectionsProperly()
    //{
    //    MySqlConnection con = new MySqlConnection(st.GetConnectionString(true));
    //    MySqlCommand cmd = new MySqlCommand("show global status like 'aborted_clients'", con);
    //    con.Open();
    //    MySqlDataReader r = cmd.ExecuteReader();
    //    r.Read();
    //    int numClientsAborted = r.GetInt32(1);
    //    r.Close();

    //    AppDomain appDomain = FullTrustSandbox.CreateFullTrustDomain();


    //    FullTrustSandbox sandbox = (FullTrustSandbox)appDomain.CreateInstanceAndUnwrap(
    //        typeof(FullTrustSandbox).Assembly.FullName,
    //        typeof(FullTrustSandbox).FullName);

    //    try
    //    {
    //        for (int i = 0; i < 200; i++)
    //        {
    //            MySqlConnection connection = sandbox.TryOpenConnection(st.GetPoolingConnectionString());
    //            Assert.NotNull(connection);
    //            Assert.True(connection.State == ConnectionState.Open);
    //            connection.Close();
    //        }
    //    }
    //    finally
    //    {
    //        AppDomain.Unload(appDomain);
    //    }
    //    r = cmd.ExecuteReader();
    //    r.Read();
    //    int numClientsAborted2 = r.GetInt32(1);
    //    r.Close();
    //    Assert.AreEqual(numClientsAborted, numClientsAborted2);
    //    con.Close();
    //}

    class ConnectionClosedCheck
    {
      public bool closed = false;
      public void stateChangeHandler(object sender, StateChangeEventArgs e)
      {
        if (e.CurrentState == ConnectionState.Closed)
          closed = true;
      }
    }
  }
}
