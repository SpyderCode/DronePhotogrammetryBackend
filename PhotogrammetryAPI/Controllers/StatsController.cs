using Microsoft.AspNetCore.Mvc;
using PhotogrammetryAPI.Services;

namespace PhotogrammetryAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatsController : ControllerBase
{
    private readonly IRabbitMQStatsService _statsService;

    public StatsController(IRabbitMQStatsService statsService)
    {
        _statsService = statsService;
    }

    [HttpGet]
    public async Task<ActionResult<object>> GetStats()
    {
        try
        {
            var stats = await _statsService.GetQueueStatsAsync();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
