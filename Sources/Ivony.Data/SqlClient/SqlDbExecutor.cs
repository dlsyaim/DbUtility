﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Data.SqlClient;
using System.Data;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Collections;
using System.Configuration;
using System.Threading.Tasks;
using Ivony.Data.Queries;
using Ivony.Fluent;

using System.Linq;
using System.Threading;
using System.Data.Common;
using Ivony.Data.Common;

namespace Ivony.Data.SqlClient
{
  /// <summary>
  /// 用于操作 SQL Server 的数据库访问工具
  /// </summary>
  public class SqlDbExecutor : DbExecutorBase, IAsyncDbExecutor<ParameterizedQuery>, IAsyncDbExecutor<StoredProcedureQuery>, IDbTransactionProvider<SqlDbExecutor>
  {



    /// <summary>
    /// 获取当前连接字符串
    /// </summary>
    protected string ConnectionString
    {
      get;
      private set;
    }


    /// <summary>
    /// 创建 SqlServer 数据库查询执行程序
    /// </summary>
    /// <param name="connectionString">连接字符串</param>
    /// <param name="configuration">当前要使用的数据库配置信息</param>
    public SqlDbExecutor( string connectionString, SqlDbConfiguration configuration )
      : base( configuration )
    {
      if ( connectionString == null )
        throw new ArgumentNullException( "connectionString" );

      if ( configuration == null )
        throw new ArgumentNullException( "configuration" );



      ConnectionString = connectionString;
      Configuration = configuration;
    }


    /// <summary>
    /// 当前要使用的数据库配置信息
    /// </summary>
    protected SqlDbConfiguration Configuration
    {
      get;
      private set;
    }


    /// <summary>
    /// 创建数据库事务上下文
    /// </summary>
    /// <returns>数据库事务上下文</returns>
    public SqlDbTransactionContext CreateTransaction()
    {
      return new SqlDbTransactionContext( ConnectionString, Configuration );
    }


    IDbTransactionContext<SqlDbExecutor> IDbTransactionProvider<SqlDbExecutor>.CreateTransaction()
    {
      return CreateTransaction();
    }



    /// <summary>
    /// 执行查询命令并返回执行上下文
    /// </summary>
    /// <param name="command">查询命令</param>
    /// <param name="tracing">用于追踪查询过程的追踪器</param>
    /// <returns>查询执行上下文</returns>
    protected virtual IDbExecuteContext Execute( SqlCommand command, IDbTracing tracing = null )
    {

      try
      {
        TryExecuteTracing( tracing, t => t.OnExecuting( command ) );


        var connection = new SqlConnection( ConnectionString );
        connection.Open();
        command.Connection = connection;

        var reader = command.ExecuteReader();
        var context = new SqlDbExecuteContext( connection, reader, tracing );

        TryExecuteTracing( tracing, t => t.OnLoadingData( context ) );

        return context;

      }
      catch ( DbException exception )
      {
        TryExecuteTracing( tracing, t => t.OnException( exception ) );
        throw;
      }
    }


    /// <summary>
    /// 异步执行查询命令并返回执行上下文
    /// </summary>
    /// <param name="command">查询命令</param>
    /// <param name="token">取消指示</param>
    /// <param name="tracing">用于追踪查询过程的追踪器</param>
    /// <returns>查询执行上下文</returns>
    protected virtual async Task<IAsyncDbExecuteContext> ExecuteAsync( SqlCommand command, CancellationToken token, IDbTracing tracing = null )
    {
      try
      {
        TryExecuteTracing( tracing, t => t.OnExecuting( command ) );

        var connection = new SqlConnection( ConnectionString );
        await connection.OpenAsync( token );
        command.Connection = connection;


        var reader = await command.ExecuteReaderAsync( token );
        var context = new SqlDbExecuteContext( connection, reader, tracing );

        TryExecuteTracing( tracing, t => t.OnLoadingData( context ) );

        return context;
      }
      catch ( DbException exception )
      {
        TryExecuteTracing( tracing, t => t.OnException( exception ) );
        throw;
      }
    }



    IDbExecuteContext IDbExecutor<ParameterizedQuery>.Execute( ParameterizedQuery query )
    {
      return Execute( CreateCommand( query ), TryCreateTracing( this, query ) );
    }

    Task<IAsyncDbExecuteContext> IAsyncDbExecutor<ParameterizedQuery>.ExecuteAsync( ParameterizedQuery query, CancellationToken token )
    {
      return ExecuteAsync( CreateCommand( query ), token, TryCreateTracing( this, query ) );
    }


    /// <summary>
    /// 从参数化查询创建查询命令对象
    /// </summary>
    /// <param name="query">参数化查询对象</param>
    /// <returns>SQL 查询命令对象</returns>
    protected SqlCommand CreateCommand( ParameterizedQuery query )
    {
      var command = new SqlParameterizedQueryParser().Parse( query );

      if ( Configuration.QueryExecutingTimeout != null )
        command.CommandTimeout = (int) Configuration.QueryExecutingTimeout.Value.TotalSeconds;


      return command;
    }



    IDbExecuteContext IDbExecutor<StoredProcedureQuery>.Execute( StoredProcedureQuery query )
    {
      return Execute( CreateCommand( query ), TryCreateTracing( this, query ) );
    }

    Task<IAsyncDbExecuteContext> IAsyncDbExecutor<StoredProcedureQuery>.ExecuteAsync( StoredProcedureQuery query, CancellationToken token )
    {
      return ExecuteAsync( CreateCommand( query ), token, TryCreateTracing( this, query ) );
    }


    /// <summary>
    /// 通过存储过程查询创建 SqlCommand 对象
    /// </summary>
    /// <param name="query">存储过程查询对象</param>
    /// <returns>SQL 查询命令对象</returns>
    protected SqlCommand CreateCommand( StoredProcedureQuery query )
    {
      var command = new SqlCommand( query.Name );
      command.CommandType = CommandType.StoredProcedure;
      query.Parameters.ForAll( pair => command.Parameters.AddWithValue( pair.Key, pair.Value ) );

      
      if ( Configuration.QueryExecutingTimeout != null )
        command.CommandTimeout = (int) Configuration.QueryExecutingTimeout.Value.TotalSeconds;

      
      return command;
    }
  }



  internal class SqlDbExecutorWithTransaction : SqlDbExecutor
  {
    public SqlDbExecutorWithTransaction( SqlDbTransactionContext transaction, SqlDbConfiguration configuration )
      : base( transaction.Connection.ConnectionString, configuration )
    {
      TransactionContext = transaction;
    }


    /// <summary>
    /// 当前所处的事务
    /// </summary>
    protected SqlDbTransactionContext TransactionContext
    {
      get;
      private set;
    }


    /// <summary>
    /// 重写 ExecuteAsync 方法，在事务中异步执行查询
    /// </summary>
    /// <param name="command">要执行的查询命令</param>
    /// <param name="token">取消指示</param>
    /// <param name="tracing">用于追踪的追踪器</param>
    /// <returns>查询执行上下文</returns>
    protected sealed override async Task<IAsyncDbExecuteContext> ExecuteAsync( SqlCommand command, CancellationToken token, IDbTracing tracing = null )
    {
      try
      {
        TryExecuteTracing( tracing, t => t.OnExecuting( command ) );

        command.Connection = TransactionContext.Connection;
        command.Transaction = TransactionContext.Transaction;

        var reader = await command.ExecuteReaderAsync( token );
        var context = new SqlDbExecuteContext( TransactionContext, reader, tracing );

        TryExecuteTracing( tracing, t => t.OnLoadingData( context ) );

        return context;
      }
      catch ( DbException exception )
      {
        TryExecuteTracing( tracing, t => t.OnException( exception ) );
        throw;
      }

    }


    /// <summary>
    /// 执行查询命令并返回执行上下文
    /// </summary>
    /// <param name="command">查询命令</param>
    /// <param name="tracing">用于追踪查询过程的追踪器</param>
    /// <returns>查询执行上下文</returns>
    protected sealed override IDbExecuteContext Execute( SqlCommand command, IDbTracing tracing = null )
    {
      try
      {
        TryExecuteTracing( tracing, t => t.OnExecuting( command ) );

        command.Connection = TransactionContext.Connection;
        command.Transaction = TransactionContext.Transaction;

        var reader = command.ExecuteReader();
        var context = new SqlDbExecuteContext( TransactionContext, reader, tracing );

        TryExecuteTracing( tracing, t => t.OnLoadingData( context ) );

        return context;
      }
      catch ( DbException exception )
      {
        TryExecuteTracing( tracing, t => t.OnException( exception ) );
        throw;
      }
    }

  }

}