using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Mangrove.Server.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/credentials")]
[Authorize(Roles = "Admin")]
public sealed class CredentialsController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly CredentialProtector _protector;

    public CredentialsController(MangroveDbContext db, CredentialProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CredentialDto>>> List(CancellationToken ct)
    {
        var creds = await _db.Credentials
            .Select(c => new CredentialDto(c.Id, c.Label, c.Username, c.Domain, c.Kind))
            .ToListAsync(ct);
        return Ok(creds);
    }

    [HttpPost]
    public async Task<ActionResult<CredentialDto>> Create(CreateCredentialRequest req, CancellationToken ct)
    {
        var cred = new Credential
        {
            Label = req.Label,
            Username = req.Username,
            Domain = req.Domain,
            Kind = req.Kind,
            PasswordEnc = _protector.Encrypt(req.Password ?? string.Empty),
        };
        _db.Credentials.Add(cred);
        await _db.SaveChangesAsync(ct);
        return Ok(new CredentialDto(cred.Id, cred.Label, cred.Username, cred.Domain, cred.Kind));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CredentialDto>> Update(int id, CreateCredentialRequest req, CancellationToken ct)
    {
        var cred = await _db.Credentials.FindAsync(new object[] { id }, ct);
        if (cred is null) return NotFound();

        cred.Label = req.Label;
        cred.Username = req.Username;
        cred.Domain = req.Domain;
        cred.Kind = req.Kind;
        if (!string.IsNullOrEmpty(req.Password))
            cred.PasswordEnc = _protector.Encrypt(req.Password);

        await _db.SaveChangesAsync(ct);
        return Ok(new CredentialDto(cred.Id, cred.Label, cred.Username, cred.Domain, cred.Kind));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var cred = await _db.Credentials.FindAsync(new object[] { id }, ct);
        if (cred is null) return NotFound();
        _db.Credentials.Remove(cred);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
