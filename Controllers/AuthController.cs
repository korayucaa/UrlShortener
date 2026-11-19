
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;
using UrlShortener.Api.Dtos;
using UrlShortener.Api.Models;
using UrlShortener.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace UrlShortener.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly JwtService _jwtService;

        public AuthController(
            AppDbContext context,
            IPasswordHasher<User> passwordHasher,
            JwtService jwtService)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _jwtService = jwtService;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [Produces("application/json")]

        public async Task<IActionResult> Register(
            RegisterRequest request)
        {
            var usernameExists = await _context.Users
                .AnyAsync(u => u.Username == request.Username);

            if (usernameExists)
            {
                return Conflict("Bu kullanıcı adı zaten kullanılıyor.");
            }

            var emailExists = await _context.Users
                .AnyAsync(u => u.Email == request.Email);

            if (emailExists)
            {
                return Conflict("Bu e-posta adresi zaten kullanılıyor.");
            }

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                Role = "User"
            };

            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                request.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Kullanıcı başarıyla oluşturuldu.",
                user.Id,
                user.Username,
                user.Email
            });
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [Produces("application/json")]
        public async Task<IActionResult> Login(
            LoginRequest request)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == request.Email);

            if (user == null)
            {
                return Unauthorized("E-posta veya parola hatalı.");
            }

            var passwordResult = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password);

            if (passwordResult == PasswordVerificationResult.Failed)
            {
                return Unauthorized("E-posta veya parola hatalı.");
            }

            var token = _jwtService.GenerateToken(user);

            return Ok(new
            {
                message = "Giriş başarılı.",
                token,
                user.Id,
                user.Username,
                user.Email
            });
        }
    }
}
