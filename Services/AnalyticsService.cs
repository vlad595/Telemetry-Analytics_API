using System;
using Telemetry_Analytics_API.DTO;
using Dapper;
using ClickHouse.Client.ADO;

namespace Telemetry_Analytics_API.Services
{
    public interface IAnalyticsService
    {
        Task<IEnumerable<dynamic>> BuildCustomAnalyticsAsync(DynamicAnalyticsRequest request);

        Task<IEnumerable<DailyPeakDto>> GetDailyPeakLoadsAsync(Guid agentId, int daysAgo);
        Task<IEnumerable<NetworkTrendDto>> GetNetworkTrafficTrendsAsync(Guid agentId, int hoursAgo);
        Task<IEnumerable<AnomalyDto>> GetCriticalAnomaliesAsync(Guid agentId, int hoursAgo);
        Task<DiskConsumptionDto?> GetDiskConsumptionRateAsync(Guid agentId, int daysAgo);
        Task<HealthScoreDto?> GetSystemHealthScoreAsync(Guid agentId, int hoursAgo);
    }
    public class AnalyticsService : IAnalyticsService
    {
        private readonly string _connectionString;
        
        private readonly HashSet<string> _allowedMetrics = new(StringComparer.OrdinalIgnoreCase) 
        { 
            "CpuUsage", "MemoryUsage", "DiskSpace", "NetworkRxBytes", "NetworkTxBytes" 
        };

        public AnalyticsService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("ClickHouse") 
                ?? throw new InvalidOperationException("ClickHouse ConnectionString is missing");
        }

        public async Task<IEnumerable<dynamic>> BuildCustomAnalyticsAsync(DynamicAnalyticsRequest request)
        {
            if (request.Rules == null || !request.Rules.Any())
                throw new ArgumentException("Must provide at least one rule");

            var selectClauses = new List<string>();

            foreach (var rule in request.Rules)
            {
                if (!_allowedMetrics.Contains(rule.MetricName))
                    throw new ArgumentException($"Invalid metric name: {rule.MetricName}");

                var alias = string.IsNullOrWhiteSpace(rule.Alias) ? $"{rule.Aggregation}_{rule.MetricName}" : rule.Alias;
                
                string sqlFunc = rule.Aggregation switch
                {
                    AggregationType.Avg => $"AVG({rule.MetricName})",
                    AggregationType.Max => $"MAX({rule.MetricName})",
                    AggregationType.Min => $"MIN({rule.MetricName})",
                    AggregationType.Sum => $"SUM({rule.MetricName})",
                    AggregationType.P95 => $"quantile(0.95)({rule.MetricName})",
                    AggregationType.P99 => $"quantile(0.99)({rule.MetricName})",
                    _ => throw new NotImplementedException()
                };

                var safeAlias = new string(alias.Where(char.IsLetterOrDigit).ToArray());
                
                selectClauses.Add($"{sqlFunc} AS {safeAlias}");
            }

            string intervalSql = request.GroupingInterval switch
            {
                TimeInterval.Minute => "INTERVAL 1 minute",
                TimeInterval.FiveMinutes => "INTERVAL 5 minute",
                TimeInterval.Hour => "INTERVAL 1 hour",
                TimeInterval.Day => "INTERVAL 1 day",
                _ => "INTERVAL 1 hour"
            };

            var query = $@"
                SELECT 
                    toStartOfInterval(Timestamp, {intervalSql}) AS Period,
                    {string.Join(",\n                    ", selectClauses)}
                FROM SystemMetrics
                WHERE AgentId = @AgentId AND Timestamp >= now() - INTERVAL @HoursAgo HOUR
                GROUP BY Period
                ORDER BY Period";

            using var connection = new ClickHouseConnection(_connectionString);
            
            return await connection.QueryAsync<dynamic>(query, new { request.AgentId, request.HoursAgo });
        }
        
        public async Task<IEnumerable<DailyPeakDto>> GetDailyPeakLoadsAsync(Guid agentId, int daysAgo)
        {
            var query = @"
                SELECT 
                    toStartOfDay(Timestamp) AS Period,
                    quantile(0.95)(CpuUsage) AS P95Cpu,
                    quantile(0.95)(MemoryUsage) AS P95Memory
                FROM SystemMetrics
                WHERE AgentId = @AgentId AND Timestamp >= now() - INTERVAL @DaysAgo DAY
                GROUP BY Period
                ORDER BY Period";

            using var connection = new ClickHouseConnection(_connectionString);
            return await connection.QueryAsync<DailyPeakDto>(query, new { AgentId = agentId, DaysAgo = daysAgo });
        }

        public async Task<IEnumerable<NetworkTrendDto>> GetNetworkTrafficTrendsAsync(Guid agentId, int hoursAgo)
        {
            var query = @"
                SELECT 
                    toStartOfInterval(Timestamp, INTERVAL 1 hour) AS Period,
                    SUM(NetworkRxBytes) / 1048576.0 AS TotalRxMegabytes,
                    SUM(NetworkTxBytes) / 1048576.0 AS TotalTxMegabytes
                FROM SystemMetrics
                WHERE AgentId = @AgentId AND Timestamp >= now() - INTERVAL @HoursAgo HOUR
                GROUP BY Period
                ORDER BY Period";

            using var connection = new ClickHouseConnection(_connectionString);
            return await connection.QueryAsync<NetworkTrendDto>(query, new { AgentId = agentId, HoursAgo = hoursAgo });
        }

        public async Task<IEnumerable<AnomalyDto>> GetCriticalAnomaliesAsync(Guid agentId, int hoursAgo)
        {
            var query = @"
                SELECT 
                    Timestamp AS ExactTime,
                    CpuUsage,
                    MemoryUsage
                FROM SystemMetrics
                WHERE AgentId = @AgentId 
                  AND Timestamp >= now() - INTERVAL @HoursAgo HOUR
                  AND CpuUsage > 90 
                  AND MemoryUsage > 90
                ORDER BY Timestamp DESC";

            using var connection = new ClickHouseConnection(_connectionString);
            return await connection.QueryAsync<AnomalyDto>(query, new { AgentId = agentId, HoursAgo = hoursAgo });
        }

        public async Task<DiskConsumptionDto?> GetDiskConsumptionRateAsync(Guid agentId, int daysAgo)
        {
            var query = @"
                SELECT 
                    argMin(DiskSpace, Timestamp) AS StartDiskSpace,
                    argMax(DiskSpace, Timestamp) AS EndDiskSpace
                FROM SystemMetrics
                WHERE AgentId = @AgentId AND Timestamp >= now() - INTERVAL @DaysAgo DAY";

            using var connection = new ClickHouseConnection(_connectionString);
            return await connection.QueryFirstOrDefaultAsync<DiskConsumptionDto>(query, new { AgentId = agentId, DaysAgo = daysAgo });
        }

        public async Task<HealthScoreDto?> GetSystemHealthScoreAsync(Guid agentId, int hoursAgo)
        {
            var query = @"
                SELECT 
                    count() AS TotalMeasurements,
                    countIf(CpuUsage > 70) AS OverloadedMeasurements
                FROM SystemMetrics
                WHERE AgentId = @AgentId AND Timestamp >= now() - INTERVAL @HoursAgo HOUR";

            using var connection = new ClickHouseConnection(_connectionString);
            return await connection.QueryFirstOrDefaultAsync<HealthScoreDto>(query, new { AgentId = agentId, HoursAgo = hoursAgo });
        }
    }
}