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
        Task<IEnumerable<MetricTimelinePointDto>> GetMetricTimelineAsync(Guid agentId, int hoursAgo, int intervalMinutes);
        Task<MetricSnapshotDto?> GetMetricSnapshotAsync(Guid agentId, DateTime at, int maxHistoryHours);
        Task<MetricTimeRangeDto?> GetMetricTimeRangeAsync(Guid agentId, int maxHistoryHours);
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
                WHERE AgentId = @AgentId AND Timestamp >= @From
                GROUP BY Period
                ORDER BY Period";

            using var connection = new ClickHouseConnection(_connectionString);
            
            var from = DateTime.UtcNow.AddHours(-request.HoursAgo);
            return await connection.QueryAsync<dynamic>(query, new { request.AgentId, From = from });
        }
        
        public async Task<IEnumerable<DailyPeakDto>> GetDailyPeakLoadsAsync(Guid agentId, int daysAgo)
        {
            var query = @"
                SELECT 
                    toStartOfDay(Timestamp) AS Period,
                    quantile(0.95)(CpuUsage) AS P95Cpu,
                    quantile(0.95)(MemoryUsage) AS P95Memory
                FROM SystemMetrics
                WHERE AgentId = @AgentId AND Timestamp >= @From
                GROUP BY Period
                ORDER BY Period";

            using var connection = new ClickHouseConnection(_connectionString);
            return await connection.QueryAsync<DailyPeakDto>(query, new { AgentId = agentId, From = DateTime.UtcNow.AddDays(-daysAgo) });
        }

        public async Task<IEnumerable<NetworkTrendDto>> GetNetworkTrafficTrendsAsync(Guid agentId, int hoursAgo)
        {
            var query = @"
                SELECT 
                    toStartOfInterval(Timestamp, INTERVAL 1 hour) AS Period,
                    SUM(NetworkRxBytes) / 1048576.0 AS TotalRxMegabytes,
                    SUM(NetworkTxBytes) / 1048576.0 AS TotalTxMegabytes
                FROM SystemMetrics
                WHERE AgentId = @AgentId AND Timestamp >= @From
                GROUP BY Period
                ORDER BY Period";

            using var connection = new ClickHouseConnection(_connectionString);
            return await connection.QueryAsync<NetworkTrendDto>(query, new { AgentId = agentId, From = DateTime.UtcNow.AddHours(-hoursAgo) });
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
                  AND Timestamp >= @From
                  AND CpuUsage > 90 
                  AND MemoryUsage > 90
                ORDER BY Timestamp DESC";

            using var connection = new ClickHouseConnection(_connectionString);
            return await connection.QueryAsync<AnomalyDto>(query, new { AgentId = agentId, From = DateTime.UtcNow.AddHours(-hoursAgo) });
        }

        public async Task<DiskConsumptionDto?> GetDiskConsumptionRateAsync(Guid agentId, int daysAgo)
        {
            var query = @"
                SELECT 
                    argMin(DiskSpace, Timestamp) AS StartDiskSpace,
                    argMax(DiskSpace, Timestamp) AS EndDiskSpace
                FROM SystemMetrics
                WHERE AgentId = @AgentId AND Timestamp >= @From";

            using var connection = new ClickHouseConnection(_connectionString);
            return await connection.QueryFirstOrDefaultAsync<DiskConsumptionDto>(query, new { AgentId = agentId, From = DateTime.UtcNow.AddDays(-daysAgo) });
        }

        public async Task<HealthScoreDto?> GetSystemHealthScoreAsync(Guid agentId, int hoursAgo)
        {
            var query = @"
                SELECT 
                    count() AS TotalMeasurements,
                    countIf(CpuUsage > 70) AS OverloadedMeasurements
                FROM SystemMetrics
                WHERE AgentId = @AgentId AND Timestamp >= @From";

            using var connection = new ClickHouseConnection(_connectionString);
            return await connection.QueryFirstOrDefaultAsync<HealthScoreDto>(query, new { AgentId = agentId, From = DateTime.UtcNow.AddHours(-hoursAgo) });
        }

        public async Task<IEnumerable<MetricTimelinePointDto>> GetMetricTimelineAsync(Guid agentId, int hoursAgo, int intervalMinutes)
        {
            var query = $@"
                SELECT
                    toStartOfInterval(Timestamp, INTERVAL {intervalMinutes} MINUTE) AS Period,
                    avg(CpuUsage) AS CpuUsage,
                    avg(MemoryUsage) AS MemoryUsage,
                    sum(NetworkRxBytes) / 1048576.0 AS NetworkRxMegabytes,
                    sum(NetworkTxBytes) / 1048576.0 AS NetworkTxMegabytes
                FROM SystemMetrics
                WHERE AgentId = @AgentId
                  AND Timestamp >= @From
                  AND Timestamp <= @To
                GROUP BY Period
                ORDER BY Period";

            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddHours(-hoursAgo);
            using var connection = new ClickHouseConnection(_connectionString);
            return await connection.QueryAsync<MetricTimelinePointDto>(query, new { AgentId = agentId, From = from, To = to });
        }

        public async Task<MetricSnapshotDto?> GetMetricSnapshotAsync(Guid agentId, DateTime at, int maxHistoryHours)
        {
            const string beforeQuery = @"
                SELECT Timestamp, CpuUsage, MemoryUsage, DiskSpace, NetworkRxBytes, NetworkTxBytes
                FROM SystemMetrics
                WHERE AgentId = @AgentId
                  AND Timestamp >= @From
                  AND Timestamp <= @At
                ORDER BY Timestamp DESC
                LIMIT 1";
            const string afterQuery = @"
                SELECT Timestamp, CpuUsage, MemoryUsage, DiskSpace, NetworkRxBytes, NetworkTxBytes
                FROM SystemMetrics
                WHERE AgentId = @AgentId
                  AND Timestamp > @At
                  AND Timestamp <= @To
                ORDER BY Timestamp ASC
                LIMIT 1";

            var utcAt = DateTime.SpecifyKind(at, DateTimeKind.Utc);
            var from = DateTime.UtcNow.AddHours(-maxHistoryHours);
            var to = DateTime.UtcNow;
            using var connection = new ClickHouseConnection(_connectionString);
            var parameters = new { AgentId = agentId, From = from, At = utcAt, To = to };
            var before = await connection.QueryFirstOrDefaultAsync<MetricSnapshotDto>(beforeQuery, parameters);
            var after = await connection.QueryFirstOrDefaultAsync<MetricSnapshotDto>(afterQuery, parameters);
            if (before == null) return AsUtc(after);
            if (after == null) return AsUtc(before);
            var span = (after.Timestamp - before.Timestamp).TotalMilliseconds;
            if (span <= 0) return AsUtc(before);
            if (span > TimeSpan.FromMinutes(5).TotalMilliseconds)
            {
                return AsUtc((utcAt - before.Timestamp) <= (after.Timestamp - utcAt) ? before : after);
            }
            var ratio = Math.Clamp((utcAt - before.Timestamp).TotalMilliseconds / span, 0, 1);
            return new MetricSnapshotDto
            {
                Timestamp = utcAt,
                CpuUsage = Interpolate(before.CpuUsage, after.CpuUsage, ratio),
                MemoryUsage = Interpolate(before.MemoryUsage, after.MemoryUsage, ratio),
                DiskSpace = Interpolate(before.DiskSpace, after.DiskSpace, ratio),
                NetworkRxBytes = Interpolate(before.NetworkRxBytes, after.NetworkRxBytes, ratio),
                NetworkTxBytes = Interpolate(before.NetworkTxBytes, after.NetworkTxBytes, ratio)
            };
        }

        public async Task<MetricTimeRangeDto?> GetMetricTimeRangeAsync(Guid agentId, int maxHistoryHours)
        {
            const string query = @"
                SELECT
                    min(Timestamp) AS StartTimestamp,
                    max(Timestamp) AS EndTimestamp
                FROM SystemMetrics
                WHERE AgentId = @AgentId
                  AND Timestamp >= @From
                  AND Timestamp <= @To";

            var to = DateTime.UtcNow;
            var from = to.AddHours(-maxHistoryHours);
            using var connection = new ClickHouseConnection(_connectionString);
            var result = await connection.QueryFirstOrDefaultAsync<MetricTimeRangeDto>(query, new { AgentId = agentId, From = from, To = to });
            if (result == null || result.StartTimestamp == default || result.EndTimestamp == default) return null;
            result.StartTimestamp = DateTime.SpecifyKind(result.StartTimestamp, DateTimeKind.Utc);
            result.EndTimestamp = DateTime.SpecifyKind(result.EndTimestamp, DateTimeKind.Utc);
            return result;
        }

        private static MetricSnapshotDto? AsUtc(MetricSnapshotDto? sample)
        {
            if (sample != null) sample.Timestamp = DateTime.SpecifyKind(sample.Timestamp, DateTimeKind.Utc);
            return sample;
        }

        private static double Interpolate(double before, double after, double ratio) => before + ((after - before) * ratio);
    }
}
