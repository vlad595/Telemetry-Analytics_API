using System;
using Telemetry_Analytics_API.DTO;
using Dapper;
using ClickHouse.Client.ADO;

namespace Telemetry_Analytics_API.Services
{
    public interface IAnalyticsService
    {
        Task<IEnumerable<AggregatedMetricDto>> GetAgentSummaryAsync(Guid agentId, int hoursAgo);
        Task<SystemStatsDto> GetSystemStatsAsync();
    }
    public class AnalyticsService : IAnalyticsService
    {
        private readonly string _connectionString;
        private readonly ILogger<AnalyticsService> _logger;

        public AnalyticsService(IConfiguration configuration, ILogger<AnalyticsService> logger)
        {
            _connectionString = configuration.GetConnectionString("ClickHouse") 
                ?? throw new ArgumentNullException("ClickHouse connection string is missing");
            _logger = logger;
        }

        public async Task<IEnumerable<AggregatedMetricDto>> GetAgentSummaryAsync(Guid agentId, int hoursAgo)
        {
            using var db = new ClickHouseConnection(_connectionString);

            var sql = @"
                SELECT 
                    toStartOfMinute(Timestamp) AS Period,
                    AVG(CpuUsage) AS AvgCpu,
                    MAX(CpuUsage) AS MaxCpu,
                    AVG(MemoryUsage) AS AvgMemory,
                    AVG(NetworkRxBytes) AS AvgNetworkRx,
                    AVG(NetworkTxBytes) AS AvgNetworkTx
                FROM SystemMetrics
                WHERE AgentId = @AgentId 
                AND Timestamp >= now() - INTERVAL @HoursAgo HOUR
                GROUP BY Period
                ORDER BY Period ASC;
            ";

            try
            {
                return await db.QueryAsync<AggregatedMetricDto>(sql, new { AgentId = agentId, HoursAgo = hoursAgo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Помилка при отриманні агрегованих даних для агента {AgentId}", agentId);
                throw;
            }
        }

        public async Task<SystemStatsDto> GetSystemStatsAsync()
        {
            using var db = new ClickHouseConnection(_connectionString);

            var sql = "SELECT COUNT(*) AS TotalMetricsRows, uniqExact(AgentId) AS ActiveAgents FROM SystemMetrics;";
            
            try
            {
                return await db.QuerySingleAsync<SystemStatsDto>(sql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Помилка при отриманні загальної статистики системи");
                throw;
            }
        }
    }
}