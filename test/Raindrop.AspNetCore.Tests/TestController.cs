using Microsoft.AspNetCore.Mvc;
using Raindrop.AspNetCore.Filters;

namespace Raindrop.AspNetCore.Tests;

[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    [HttpGet("hello")]
    public IActionResult Hello() => Ok(new { message = "hello world" });

    [AccessLog(Full = true)]
    [HttpGet("withFull")]
    public IActionResult WithFull() => Ok(new { message = "full response" });

    [AccessLog(Full = false)]
    [HttpGet("withoutFull")]
    public IActionResult WithoutFull() => Ok(new { message = "hidden response" });

    [HttpGet("created")]
    public IActionResult CreatedResource() => Created("/api/test/created", new { id = 1 });

    [HttpGet("error")]
    public IActionResult Error() => throw new InvalidOperationException("test error");
}
