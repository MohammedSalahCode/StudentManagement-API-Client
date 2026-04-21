using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;

namespace StudentApiConsoleClient
{
    class Program
    {
        private const string BaseUrl = "https://localhost:7244/";
        private const string Email = "fadi.khail@student.com";
        private const string Password = "password1";

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Student API Console Client (JWT) ===");
            Console.WriteLine();

            using var http = CreateHttpClientForLocalDev(BaseUrl);

            var token = await LoginAndGetTokenAsync(http, Email, Password);

            if (string.IsNullOrWhiteSpace(token))
            {

                Console.WriteLine("Login failed.");
                return;
            }

            Console.WriteLine("Login succeeded.");
            Console.WriteLine($"Token (first 30 chars): {token[..30]}...");

            Console.WriteLine("Calling API WITHOUT token (expected 401)");
            await CallGetAllStudentsAsync(http, null);

            Console.WriteLine("\nCalling API WITH token (expected 200)");
            await CallGetAllStudentsAsync(http, token);

            Console.ReadKey();
        }


        // ===============
        // Helper Methods
        // ===============


        // Creates HttpClient for local HTTPS development (self-signed certificates allowed)
        static HttpClient CreateHttpClientForLocalDev(string baseUrl)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    (message, certificate, chain, sslErrors) =>
                        sslErrors == SslPolicyErrors.None ||
                        sslErrors == SslPolicyErrors.RemoteCertificateChainErrors
            };

            return new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl)
            };
        }


        // Calls the login endpoint and returns JWT token if valid.
        static async Task<string> LoginAndGetTokenAsync(HttpClient http, string email, string password)
        {
            var request = new LoginRequest
            {
                Email = email,
                Password = password
            };

            var response = await http.PostAsJsonAsync("/api/Auth/login", request);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Console.WriteLine("Invalid credentials.");
                return "";
            }

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Login failed: {response.StatusCode}");
                return "";
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();

            return tokenResponse?.Token ?? "";
        }


        // Calls secured endpoint using JWT in Authorization header
        static async Task CallGetAllStudentsAsync(HttpClient http, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/Students/All");

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Console.WriteLine("401 Unauthorized");
                return;
            }

            var students = await response.Content.ReadFromJsonAsync<List<StudentDto>>();

            Console.WriteLine($"{students.Count} students returned:");
            foreach (var s in students)
            {
                Console.WriteLine($"- {s.Name} (Age: {s.Age}, Grade: {s.Grade})");
            }
        }
    }


    class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    class TokenResponse
    {
        public string Token { get; set; }
    }

    class StudentDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        public int Grade { get; set; }
    }
}
