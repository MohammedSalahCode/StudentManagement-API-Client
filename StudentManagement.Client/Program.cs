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

        private const string Email = "ali.ahmed@student.com";
        private const string Password = "admin123";

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Student API Console Client (Access + Refresh Tokens) ===");
            Console.WriteLine();

            using var http = CreateHttpClientForLocalDev(BaseUrl);

            var tokenPair = await LoginAsync(http, Email, Password);

            if (tokenPair == null ||
                string.IsNullOrWhiteSpace(tokenPair.AccessToken) ||
                string.IsNullOrWhiteSpace(tokenPair.RefreshToken))
            {
                Console.WriteLine("Login failed.");
                return;
            }

            var tokenState = new TokenState(tokenPair.AccessToken, tokenPair.RefreshToken);


            Console.WriteLine("Login succeeded.");

            Console.WriteLine("======================================");
            Console.WriteLine("Initial Tokens:");
            Console.WriteLine("======================================");

            Console.WriteLine($"Access Token:\n{tokenState.AccessToken}");
            Console.WriteLine();
            Console.WriteLine($"Refresh Token:\n{tokenState.RefreshToken}");
            Console.WriteLine("======================================");
            Console.WriteLine();


            Console.WriteLine("API Call: GET /api/Students/All");
            await CallGetAllStudentsWithAutoRefreshAsync(http, Email, tokenState);

            Console.ReadKey();
        }

        // ==========================
        // Helper Methods
        // ==========================

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


        static async Task<TokenResponse?> LoginAsync(HttpClient http, string email, string password)
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
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Login failed: {response.StatusCode}");
                return null;
            }

            return await response.Content.ReadFromJsonAsync<TokenResponse>();
        }


        static async Task<TokenResponse?> RefreshTokensAsync(HttpClient http, string email, string refreshToken)
        {
            var request = new RefreshRequest
            {
                Email = email,
                RefreshToken = refreshToken
            };

            var response = await http.PostAsJsonAsync("/api/Auth/refresh", request);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Console.WriteLine("Refresh failed: Unauthorized (refresh token invalid/expired/revoked).");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Refresh failed: {response.StatusCode}");
                return null;
            }

            return await response.Content.ReadFromJsonAsync<TokenResponse>();
        }


        static async Task<HttpResponseMessage> SendWithAutoRefreshAsync(
            HttpClient http,
            HttpRequestMessage request,
            string email,
            TokenState tokenState)
        {
            ApplyBearerToken(request, tokenState.AccessToken);
            var response = await http.SendAsync(request);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
                return response;

            Console.WriteLine("Access token rejected (401). Refreshing tokens...");

            response.Dispose();

            var newTokens = await RefreshTokensAsync(http, email, tokenState.RefreshToken);
            if (newTokens == null ||
                string.IsNullOrWhiteSpace(newTokens.AccessToken) ||
                string.IsNullOrWhiteSpace(newTokens.RefreshToken))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            tokenState.AccessToken = newTokens.AccessToken;
            tokenState.RefreshToken = newTokens.RefreshToken;

            Console.WriteLine("Refresh succeeded. Retrying the original request...");
            Console.WriteLine("======================================");
            Console.WriteLine("NEW TOKENS RECEIVED AFTER REFRESH:");
            Console.WriteLine("======================================");

            Console.WriteLine($"New Access Token:\n{tokenState.AccessToken}");
            Console.WriteLine();
            Console.WriteLine($"New Refresh Token:\n{tokenState.RefreshToken}");

            Console.WriteLine("======================================");
            Console.WriteLine();


            using var retryRequest = CloneRequest(request);
            ApplyBearerToken(retryRequest, tokenState.AccessToken);

            return await http.SendAsync(retryRequest);
        }

        static async Task CallGetAllStudentsWithAutoRefreshAsync(
            HttpClient http,
            string email,
            TokenState tokenState)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/Students/All");

            var response = await SendWithAutoRefreshAsync(http, request, email, tokenState);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Console.WriteLine("401 Unauthorized. Access token expired and refresh failed (need re-login).");
                return;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Console.WriteLine("403 Forbidden. You are authenticated, but not allowed to do this action.");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Request failed: {response.StatusCode}");
                return;
            }

            var students = await response.Content.ReadFromJsonAsync<List<StudentDto>>();
            if (students == null)
            {
                Console.WriteLine("No data returned.");
                return;
            }

            Console.WriteLine($"{students.Count} students returned:");
            foreach (var s in students)
            {
                Console.WriteLine($"- {s.Name} (Age: {s.Age}, Grade: {s.Grade})");
            }

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("Current Token State After Request:");
            Console.WriteLine("======================================");

            Console.WriteLine($"Access Token:\n{tokenState.AccessToken}");
            Console.WriteLine();
            Console.WriteLine($"Refresh Token:\n{tokenState.RefreshToken}");

            Console.WriteLine("======================================");
            Console.WriteLine();

        }

        // --------------------------
        // Token utilities
        // --------------------------
        static void ApplyBearerToken(HttpRequestMessage request, string accessToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        static HttpRequestMessage CloneRequest(HttpRequestMessage original)
        {
            var clone = new HttpRequestMessage(original.Method, original.RequestUri);

            foreach (var header in original.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (original.Content != null)
                clone.Content = original.Content;

            return clone;
        }


        class TokenState
        {
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }

            public TokenState(string accessToken, string refreshToken)
            {
                AccessToken = accessToken;
                RefreshToken = refreshToken;
            }
        }

        class LoginRequest
        {
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
        }

        class RefreshRequest
        {
            public string Email { get; set; } = "";
            public string RefreshToken { get; set; } = "";
        }

        class TokenResponse
        {
            public string AccessToken { get; set; } = "";
            public string RefreshToken { get; set; } = "";
        }

        class StudentDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Age { get; set; }
            public int Grade { get; set; }
        }

    }

}
