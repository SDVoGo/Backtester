using Microsoft.AspNetCore.Mvc;
using Pine.Core.Lexing;

[ApiController]
[Route("api/lexer")]
public class LexerController : ControllerBase
{
    [HttpPost("scan")]
    public IActionResult Scan(string source)
    {
        try
        {
            return Ok(Lexer.Tokenize(source));
        }
        catch (LexerException ex)
        {
            return BadRequest(new { ex.Message, ex.Line, ex.Column });
        }
    }

    [HttpPost("scan001")]
    public IActionResult Scan001()
        => Scan("   1 +    1  * 9.98  - 12 % (\"alfa\")");

    [HttpPost("test002")]
    public IActionResult Test002()
        => Scan(Utils.Resources.ReadFile("test002"));
}
