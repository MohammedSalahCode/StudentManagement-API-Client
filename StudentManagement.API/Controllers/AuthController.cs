using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using StudentManagement.API.DataSimulation;
using StudentManagement.API.DTOs.Auth;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace StudentManagement.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ILogger<AuthController> _logger;

        public AuthController(ILogger<AuthController> logger)
        {
            _logger = logger;
        }

        [HttpPost("login")]
        [EnableRateLimiting("AuthLimiter")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var student = StudentDataSimulation.StudentsList
                .FirstOrDefault(s => s.Email == request.Email);

            if (student == null)
            {
                _logger.LogWarning(
                "Failed login attempt (email not found). Email={Email}, IP={IP}",
                request.Email,
                ip
                );

                return Unauthorized("Invalid credentials");
            }
                

            bool isValidPassword =
                BCrypt.Net.BCrypt.Verify(request.Password, student.PasswordHash);

            if (!isValidPassword)
            {
                _logger.LogWarning(
                "Failed login attempt (bad password). Email={Email}, IP={IP}",
                request.Email,
                ip
                );

                return Unauthorized("Invalid credentials");
            }


            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, student.Id.ToString()),

                new Claim(ClaimTypes.Email, student.Email),

                new Claim(ClaimTypes.Role, student.Role)
            };


            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("THIS_IS_A_VERY_SECRET_KEY_123456"));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);


            var token = new JwtSecurityToken(
                issuer: "StudentManagementAPI",
                audience: "StudentApiUsers",
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30), 
                signingCredentials: creds
            );

            var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

            var refreshToken = GenerateRefreshToken();

            student.RefreshTokenHash = BCrypt.Net.BCrypt.HashPassword(refreshToken);
            student.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);
            student.RefreshTokenRevokedAt = null;


            _logger.LogInformation(
             "Successful login. UserId={UserId}, Email={Email}, IP={IP}",
             student.Id,
             student.Email,
             ip
            );

            return Ok(new TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken
            });

        }


        private static string GenerateRefreshToken()
        {
            var bytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }


        [HttpPost("refresh")]
        [EnableRateLimiting("AuthLimiter")]
        public IActionResult Refresh([FromBody] RefreshRequest request)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var student = StudentDataSimulation.StudentsList
                .FirstOrDefault(s => s.Email == request.Email);

            if (student == null)
            {
                _logger.LogWarning(
                    "Invalid refresh attempt (email not found). Email={Email}, IP={IP}",
                    request.Email,
                    ip
                );

                return Unauthorized("Invalid refresh request");
            }

            if (student.RefreshTokenRevokedAt != null)
            {
                _logger.LogWarning(
                    "Refresh attempt using revoked token. UserId={UserId}, Email={Email}, IP={IP}",
                    student.Id,
                    student.Email,
                    ip
                );

                return Unauthorized("Refresh token is revoked");
            }

            if (student.RefreshTokenExpiresAt == null || student.RefreshTokenExpiresAt <= DateTime.UtcNow)
            {
                _logger.LogWarning(
                    "Refresh attempt using expired token. UserId={UserId}, Email={Email}, IP={IP}",
                    student.Id,
                    student.Email,
                    ip
                );

                return Unauthorized("Refresh token expired");
            }

            bool refreshValid = BCrypt.Net.BCrypt.Verify(request.RefreshToken, student.RefreshTokenHash);
            if (!refreshValid)
            {
                _logger.LogWarning(
                    "Invalid refresh token attempt. UserId={UserId}, Email={Email}, IP={IP}",
                    student.Id,
                    student.Email,
                    ip
                );

                return Unauthorized("Invalid refresh token");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, student.Id.ToString()),
                new Claim(ClaimTypes.Email, student.Email),
                new Claim(ClaimTypes.Role, student.Role)
            };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("THIS_IS_A_VERY_SECRET_KEY_123456"));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var jwt = new JwtSecurityToken(
                issuer: "StudentManagementAPI",
                audience: "StudentApiUsers",
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: creds
            );

            var newAccessToken = new JwtSecurityTokenHandler().WriteToken(jwt);

            // Rotation
            var newRefreshToken = GenerateRefreshToken();
            student.RefreshTokenHash = BCrypt.Net.BCrypt.HashPassword(newRefreshToken);
            student.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);
            student.RefreshTokenRevokedAt = null;


            _logger.LogInformation(
                "Refresh succeeded. UserId={UserId}, Email={Email}, IP={IP}",
                student.Id,
                student.Email,
                ip
            );


            return Ok(new TokenResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken
            });
        }


        [HttpPost("logout")]
        public IActionResult Logout([FromBody] LogoutRequest request)
        {
            var student = StudentDataSimulation.StudentsList
                .FirstOrDefault(s => s.Email == request.Email);

            if (student == null)
                return Ok();

            bool refreshValid = BCrypt.Net.BCrypt.Verify(request.RefreshToken, student.RefreshTokenHash);
            if (!refreshValid)
                return Ok();

            student.RefreshTokenRevokedAt = DateTime.UtcNow;
            return Ok("Logged out successfully");
        }

    }
}
