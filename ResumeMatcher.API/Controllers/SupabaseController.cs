using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class SkillsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public SkillsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllSkills()
    {
        var skills = await _context.Skills.ToListAsync();
        return Ok(skills);
    }
}