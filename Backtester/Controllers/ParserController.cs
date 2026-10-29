using Microsoft.AspNetCore.Mvc;
using Pine.Core.Lexing;
using Pine.Core.Parsing;
using Pine.Core.Syntax;

[ApiController]
[Route("api/parser")]
public class ParserController : ControllerBase
{
    /// <summary>Returns the AST as S-expressions, one statement per line.</summary>
    [HttpPost("parse")]
    public IActionResult Parse(string source)
    {
        try
        {
            return Content(AstPrinter.Print(Parser.Parse(source)), "text/plain");
        }
        catch (LexerException ex)
        {
            return BadRequest(new { ex.Message, ex.Line, ex.Column });
        }
        catch (ParseException ex)
        {
            return BadRequest(new { ex.Message, ex.Line, ex.Column });
        }
    }

    [HttpPost("test002")]
    public IActionResult Test002()
        => Parse(Utils.Resources.ReadFile("test002"));

    [HttpPost("strategy001")]
    public IActionResult Strategy001()
        => Parse(Utils.Resources.ReadFile("strategy001"));
}
