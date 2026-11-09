using Microsoft.AspNetCore.Mvc;
using Telemetry_Analytics_API.Services;
using Telemetry_Analytics_API.DTO;

namespace Telemetry_Analytics_API.Controllers 
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalyticsController : ControllerBase
    {
        private readonly IAnalyticsService _analyticsService;

        public AnalyticsController(IAnalyticsService analyticsService)
        {
            _analyticsService = analyticsService;
        }

        [HttpGet("{agentId:guid}/summary")]
        [ProducesResponseType(typeof(IEnumerable<AggregatedMetricDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAgentSummary(Guid agentId, [FromQuery] int hoursAgo = 24)
        {
            if (hoursAgo <= 0 || hoursAgo > 720)
            {
                return BadRequest("Параметр hoursAgo має бути від 1 до 720.");
            }

            var result = await _analyticsService.GetAgentSummaryAsync(agentId, hoursAgo);
            return Ok(result);
        }

        [HttpGet("system-stats")]
        [ProducesResponseType(typeof(SystemStatsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetSystemStats()
        {
            var result = await _analyticsService.GetSystemStatsAsync();
            return Ok(result);
        }
    }
}