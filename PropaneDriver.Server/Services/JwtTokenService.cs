using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PropaneDriver.Server.Data;

namespace PropaneDriver.Server.Services
{
    // Issues short-lived JWTs for authenticated users. The signing key,
    // issuer, audience, and lifetime all come from the "Jwt" config block so
    // a deployment can rotate the secret without a code change.
    public class JwtTokenService
    {
        private readonly IConfiguration _configuration;

        public JwtTokenService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // One role claim per role the user holds.
        public string CreateTokenForUser(UserDbRecord user, IEnumerable<string> roles)
        {
            var jwtSection = _configuration.GetSection("Jwt");
            var signingKey = jwtSection["Key"]
                ?? throw new InvalidOperationException("Jwt:Key is not configured.");
            var issuer = jwtSection["Issuer"] ?? "PropaneDriver";
            var audience = jwtSection["Audience"] ?? "PropaneDriverClient";
            var expirationHours = double.TryParse(jwtSection["ExpirationHours"], out var hours)
                ? hours
                : 12;

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
            };
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var keyBytes = Encoding.UTF8.GetBytes(signingKey);
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(keyBytes),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddHours(expirationHours),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
