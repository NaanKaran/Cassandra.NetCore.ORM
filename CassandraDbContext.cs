﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Cassandra.Mapping;
using Cassandra.NetCore.ORM.Helpers;
using Mapper = Cassandra.Mapping.Mapper;

namespace Cassandra.NetCore.ORM
{
    public class CassandraDbContext : IDisposable
    {
        private ISession _session;
        private static Cluster _cluster;
        private int _currentBatchSize = 0;
        private object _batchLock = new object();
        private BatchStatement _currentBatch = new BatchStatement();

        public int BatchSize { get; set; } = 50;
        public bool UseBatching { get; set; } = false;


        public CassandraDbContext(string userName, string password, string cassandraContactPoint, int cassandraPort, string keySpaceName)
        {
            // Connect to cassandra cluster  (Cassandra API on Azure Cosmos DB supports only TLSv1.2)
            var options = new Cassandra.SSLOptions(SslProtocols.Tls12, true, ValidateServerCertificate);

            options.SetHostNameResolver((ipAddress) => cassandraContactPoint);
            _cluster = Cluster
                .Builder()
                .WithCredentials(userName, password)
                .WithPort(cassandraPort)
                .AddContactPoint(cassandraContactPoint)
                .WithSSL(options)
                .Build();
            CreateKeySpace(keySpaceName).Wait();
            _session =  _cluster.ConnectAsync(keySpaceName).Result;

        }
        public static bool ValidateServerCertificate
        (
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors
        )
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            Console.WriteLine("Certificate error: {0}", sslPolicyErrors);
            // Do not allow this client to communicate with unauthenticated servers.
            return false;
        }

        public void Dispose()
        {
            lock (_currentBatch)
            {
                if (_currentBatchSize > 0)
                    _session.Execute(_currentBatch);
            }

            _session.Dispose();
        }

        private async Task CreateKeySpace(string keySpace)
        {
            _session = await _cluster.ConnectAsync();
            await _session.ExecuteAsync(new SimpleStatement($"CREATE KEYSPACE IF NOT EXISTS {keySpace} WITH REPLICATION = {{ 'class' : 'NetworkTopologyStrategy', 'datacenter1' : 1 }};"));
        }

        public async Task InsertAsync<T>(T data)
        {
            try
            {
                IMapper mapper = new Mapper(_session);
                await mapper.InsertAsync<T>(data);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        public  void InsertIfNotExists<T>(T data)
        {
            try
            {
                IMapper mapper = new Mapper(_session);
                mapper.InsertIfNotExists<T>(data);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }


        public async Task<T> InsertIfNotExistsAsync<T>(T data)
        {
            try
            {
                IMapper mapper = new Mapper(_session);
                await mapper.InsertIfNotExistsAsync<T>(data);
                return data;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public IEnumerable<T> Select<T>(Expression<Func<T, bool>> predicate)
        {

            try
            {
                var tableName = typeof(T).ExtractTableName<T>();

                _session = _cluster.Connect(tableName);
                IMapper mapper = new Mapper(_session);
                var queryStatement = QueryBuilder.EvaluateQuery(predicate);
                var selectQuery = $"select * from {tableName} where {queryStatement.Statment}";

                var output = mapper.Fetch<T>(selectQuery);

                return output;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public async Task<IEnumerable<T>> SelectAsync<T>(Expression<Func<T, bool>> predicate = null)
        {
            try
            {
                var tableName = typeof(T).ExtractTableName<T>();
                IMapper mapper = new Mapper(_session);
                var selectQuery = $"select * from {tableName}";

                if (predicate != null)
                {
                    var queryStatement = QueryBuilder.EvaluateQuery(predicate);
                    selectQuery = $"select * from {tableName} where {queryStatement.Statment}";
                }
               
                var output = await mapper.FetchAsync<T>(selectQuery);

                return output;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public double Average<T, TNumericModel>(Expression<Func<T, bool>> predicate, Expression<Func<T, TNumericModel>> propertyExpression)
        {
            try
            {
                var columnName = QueryBuilder.EvaluatePropertyName(propertyExpression);

                var queryStatement = QueryBuilder.EvaluateQuery(predicate);
                var tableName = typeof(T).ExtractTableName<T>();
                var selectQuery = $"select avg({columnName}) from {tableName} where {queryStatement.Statment}";

                var statement = new SimpleStatement(selectQuery, queryStatement.Values);
                var rows = _session.Execute(statement);

                var avg = Convert.ToDouble(rows.First()[0]);

                return avg;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public async Task<double> AverageAsync<T, TNumericModel>(Expression<Func<T, bool>> predicate, Expression<Func<T, TNumericModel>> propertyExpression)
        {
            try
            {
                var columnName = QueryBuilder.EvaluatePropertyName(propertyExpression);

                var queryStatement = QueryBuilder.EvaluateQuery(predicate);
                var tableName = typeof(T).ExtractTableName<T>();
                var selectQuery = $"select avg({columnName}) from {tableName} where {queryStatement.Statment}";

                var statement = new SimpleStatement(selectQuery, queryStatement.Values);
                var rows = await _session.ExecuteAsync(statement);

                var avg = Convert.ToDouble(rows.First()[0]);

                return avg;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public double Sum<T, TNumericModel>(Expression<Func<T, bool>> predicate, Expression<Func<T, TNumericModel>> propertyExpression)
        {
            try
            {
                var columnName = QueryBuilder.EvaluatePropertyName(propertyExpression);

                var queryStatement = QueryBuilder.EvaluateQuery(predicate);
                var tableName = typeof(T).ExtractTableName<T>();
                var selectQuery = $"select sum({columnName}) from {tableName} where {queryStatement.Statment}";

                var statement = new SimpleStatement(selectQuery, queryStatement.Values);
                var rows = _session.Execute(statement);

                var sum = Convert.ToDouble(rows.First()[0]);

                return sum;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public async Task<double> SumAsync<T, TNumericModel>(Expression<Func<T, bool>> predicate, Expression<Func<T, TNumericModel>> propertyExpression)
        {
            try
            {
                var columnName = QueryBuilder.EvaluatePropertyName(propertyExpression);

                var queryStatement = QueryBuilder.EvaluateQuery(predicate);
                var tableName = typeof(T).ExtractTableName<T>();
                var selectQuery = $"select sum({columnName}) from {tableName} where {queryStatement.Statment}";

                var statement = new SimpleStatement(selectQuery, queryStatement.Values);
                var rows = await _session.ExecuteAsync(statement);

                var sum = Convert.ToDouble(rows.First()[0]);

                return sum;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public double Min<T, TNumericModel>(Expression<Func<T, bool>> predicate, Expression<Func<T, TNumericModel>> propertyExpression)
        {
            try
            {
                var columnName = QueryBuilder.EvaluatePropertyName(propertyExpression);

                var queryStatement = QueryBuilder.EvaluateQuery(predicate);
                var tableName = typeof(T).ExtractTableName<T>();
                var selectQuery = $"select min({columnName}) from {tableName} where {queryStatement.Statment}";

                var statement = new SimpleStatement(selectQuery, queryStatement.Values);
                var rows = _session.Execute(statement);
                var sum = Convert.ToDouble(rows.First()[0]);
                return sum;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        public async Task<double> MinAsync<T, TNumericModel>(Expression<Func<T, bool>> predicate, Expression<Func<T, TNumericModel>> propertyExpression)
        {
            try
            {
                var columnName = QueryBuilder.EvaluatePropertyName(propertyExpression);
                var queryStatement = QueryBuilder.EvaluateQuery(predicate);
                var tableName = typeof(T).ExtractTableName<T>();
                var selectQuery = $"select min({columnName}) from {tableName} where {queryStatement.Statment}";

                var statement = new SimpleStatement(selectQuery, queryStatement.Values);
                var rows = await _session.ExecuteAsync(statement);

                var sum = Convert.ToDouble(rows.First()[0]);
                return sum;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

        }
        public double Max<T, TNumericModel>(Expression<Func<T, bool>> predicate, Expression<Func<T, TNumericModel>> propertyExpression)
        {
            try
            {
                var columnName = QueryBuilder.EvaluatePropertyName(propertyExpression);

                var queryStatement = QueryBuilder.EvaluateQuery(predicate);
                var tableName = typeof(T).ExtractTableName<T>();
                var selectQuery = $"select max({columnName}) from {tableName} where {queryStatement.Statment}";

                var statement = new SimpleStatement(selectQuery, queryStatement.Values);
                var rows = _session.Execute(statement);

                var sum = Convert.ToDouble(rows.First()[0]);

                return sum;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }  
        public async Task<double> MaxAsync<T, TNumericModel>(Expression<Func<T, bool>> predicate, Expression<Func<T, TNumericModel>> propertyExpression)
        {
            try
            {
                var columnName = QueryBuilder.EvaluatePropertyName(propertyExpression);

                var queryStatement = QueryBuilder.EvaluateQuery(predicate);
                var tableName = typeof(T).ExtractTableName<T>();
                var selectQuery = $"select max({columnName}) from {tableName} where {queryStatement.Statment}";

                var statement = new SimpleStatement(selectQuery, queryStatement.Values);
                var rows = await _session.ExecuteAsync(statement);

                var sum = Convert.ToDouble(rows.First()[0]);

                return sum;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }


        public T SingleOrDefault<T>(Expression<Func<T, bool>> predicate)
        {
            return Select(predicate).SingleOrDefault();
        }

        public T FirstOrDefault<T>(Expression<Func<T, bool>> predicate)
        {
            return Select(predicate).FirstOrDefault();
        }

        public void AddOrUpdate<T>(T entity)
        {
            var insertStatment = CreateAddStatement(entity);

            if (UseBatching)
            {
                lock (_batchLock)
                {
                    _currentBatch.Add(insertStatment);
                    ++_currentBatchSize;
                    if (_currentBatchSize == BatchSize)
                    {
                        _session.Execute(_currentBatch);
                        _currentBatchSize = 0;
                        _currentBatch = new BatchStatement();
                    }
                }
            }
            else
            {
                _session.Execute(insertStatment);
            }
        }

        private Statement CreateAddStatement<T>(T entity)
        {
            var tableName = typeof(T).ExtractTableName<T>();

            // We are interested only in the properties we are not ignoring
            var properties = entity.GetType().GetCassandraRelevantProperties();
            var properiesNames = properties.Select(p => p.GetColumnNameMapping()).ToArray();
            var parametersSignals = properties.Select(p => "?").ToArray();
            var propertiesValues = properties.Select(p => p.GetValue(entity)).ToArray();
            var insertCql = $"insert into {tableName}({string.Join(",", properiesNames)}) values ({string.Join(",", parametersSignals)})";
            var insertStatment = new SimpleStatement(insertCql, propertiesValues);

            return insertStatment;
        }
    }
}