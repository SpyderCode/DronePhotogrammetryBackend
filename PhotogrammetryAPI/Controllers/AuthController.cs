using Microsoft.AspNetCore.Mvc;
using PhotogrammetryAPI.Models;
using PhotogrammetryAPI.Services;

namespace PhotogrammetryAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    
    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }
    
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var result = await _authService.RegisterAsync(dto);
        
        if (result == null)
            return BadRequest(new { message = "Username or email already exists" });
        
        return Ok(result);
    }
    
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var result = await _authService.LoginAsync(dto);
        
        if (result == null)
            return Unauthorized(new { message = "Invalid email or password" });
        
        return Ok(result);
    }
}
