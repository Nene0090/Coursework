using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Data.SQLite;

namespace Dashboard
{
    public class LoginRequest
    {
        public string? Login { get; set; }
        public string? Password { get; set; }
    }

    public class DashboardRequest
    {
        public string? Type { get; set; }
    }

    public class OneCExport
    {
        [JsonProperty("generated_at")]
        public DateTime GeneratedAt { get; set; }

        [JsonProperty("items")]
        public List<TicketItem> Items { get; set; } = new();
    }

    public class TicketItem
    {
        [JsonProperty("ticket_id")]
        public string? TicketId { get; set; }

        [JsonProperty("created_date")]
        public DateTime CreatedDate { get; set; }

        [JsonProperty("created_hour")]
        public int CreatedHour { get; set; }

        [JsonProperty("priority")]
        public string? Priority { get; set; }

        [JsonProperty("queue")]
        public string? Queue { get; set; }

        [JsonProperty("assignee")]
        public string? Assignee { get; set; }

        [JsonProperty("team")]
        public string? Team { get; set; }

        [JsonProperty("status")]
        public string? Status { get; set; }

        [JsonProperty("line")]
        public string? Line { get; set; }

        [JsonProperty("response_minutes")]
        public int ResponseMinutes { get; set; }

        [JsonProperty("resolve_minutes")]
        public int ResolveMinutes { get; set; }

        [JsonProperty("response_sla_breached")]
        public bool ResponseSlaBreached { get; set; }

        [JsonProperty("resolve_sla_breached")]
        public bool ResolveSlaBreached { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddCors();

            var app = builder.Build();

            app.UseCors(policy => policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());

            app.MapPost("/api/login", async (HttpContext context) =>
            {
                return await HandleLogin(context);
            });

            app.MapPost("/api/dashboard", async (HttpContext context) =>
            {
                return await HandleDashboard(context);
            });

            app.Run("http://localhost:5000");
        }

        static async Task<IResult> HandleLogin(HttpContext context)
        {
            string body = await ReadBody(context);

            LoginRequest? loginData;

            try
            {
                loginData = JsonConvert.DeserializeObject<LoginRequest>(body);
            }
            catch
            {
                return Results.Json(new
                {
                    accessLevel = 0,
                    message = "Invalid JSON"
                });
            }

            if (loginData == null ||
                string.IsNullOrWhiteSpace(loginData.Login) ||
                string.IsNullOrWhiteSpace(loginData.Password))
            {
                return Results.Json(new
                {
                    accessLevel = 0,
                    message = "Login or password is empty"
                });
            }

            int accessLevel = GetAccessLevel(loginData.Login, loginData.Password);

            return Results.Json(new
            {
                accessLevel = accessLevel
            });
        }

        static int GetAccessLevel(string login, string password)
        {
            using var connect = new SQLiteConnection(@"Data Source=D:/auth.db; Version=3;");
            connect.Open();

            using var command = connect.CreateCommand();
            command.CommandText = @"SELECT access_level FROM users WHERE login = @login AND password_hash = @password LIMIT 1";
            command.Parameters.AddWithValue("@login", login);
            command.Parameters.AddWithValue("@password", password);

            var result = command.ExecuteScalar();

            if (result == null)
                return 0;

            return Convert.ToInt32(result);
        }

        static async Task<IResult> HandleDashboard(HttpContext context)
        {
            string body = await ReadBody(context);

            DashboardRequest? request;

            try
            {
                request = JsonConvert.DeserializeObject<DashboardRequest>(body);
            }
            catch
            {
                return Results.Json(new {error = "Invalid request JSON"});
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Type))
            {
                return Results.Json(new {error = "Type is empty"});
            }

            OneCExport? export = await ReadOneCExport();

            if (export == null || export.Items == null || export.Items.Count == 0)
            {
                return Results.Json(new {error = "No data from 1C export"});
            }

            object result = request.Type switch
            {
                "sla" => AnalyzeSla(export.Items),
                "load" => AnalyzeLoad(export.Items),

                "combined_hour" => AnalyzeCombinedByHour(export.Items),
                "combined_day" => AnalyzeCombinedByDay(export.Items),
                "combined_week" => AnalyzeCombinedByWeek(export.Items),
                "combined_month" => AnalyzeCombinedByMonth(export.Items),

                "statuses" => AnalyzeStatuses(export.Items),
                "lines" => AnalyzeLines(export.Items),

                _ => new
                {
                    error = "Unknown dashboard type"
                }
            };

            return Results.Json(result);
        }

        static async Task<OneCExport?> ReadOneCExport()
        {
            string filePath = @"D:/dashboard/result.json";

            if (!File.Exists(filePath))
                return null;

            string json = await File.ReadAllTextAsync(filePath);

            return JsonConvert.DeserializeObject<OneCExport>(json);
        }

        static async Task<string> ReadBody(HttpContext context)
        {
            using var reader = new StreamReader(context.Request.Body);
            return await reader.ReadToEndAsync();
        }

        static object AnalyzeSla(List<TicketItem> tickets)
        {
            var groups = tickets.Where(t => !string.IsNullOrWhiteSpace(t.Priority)).GroupBy(t => t.Priority).OrderBy(g => g.Key).Select(g => new
                {
                    Priority = g.Key,
                    ResponseSlaOk = Math.Round(g.Count(t => !t.ResponseSlaBreached) * 100.0 / g.Count(), 1),
                    ResolveSlaOk = Math.Round(g.Count(t => !t.ResolveSlaBreached) * 100.0 / g.Count(), 1)
                }).ToList();

            return new
            {
                type = "sla", labels = groups.Select(g => g.Priority).ToArray(), datasets = new[]
                {
                    new
                    {
                        label = "Ответ в SLA, %",
                        data = groups.Select(g => g.ResponseSlaOk).ToArray()
                    },
                    new
                    {
                        label = "Решение в SLA, %",
                        data = groups.Select(g => g.ResolveSlaOk).ToArray()
                    }
                }
            };
        }
        static object AnalyzeLoad(List<TicketItem> tickets)
        {
            var groups = tickets.Where(t => !string.IsNullOrWhiteSpace(t.Assignee)).GroupBy(t => t.Assignee).OrderBy(g => g.Key).Select(g => new
                {
                    Assignee = g.Key,
                    Count = g.Count()
                }).ToList();

            return new
            {
                type = "load",
                labels = groups.Select(g => g.Assignee).ToArray(),
                datasets = new[]
                {
                    new
                    {
                        label = "Количество заявок",
                        data = groups.Select(g => g.Count).ToArray()
                    }
                }
            };
        }

        static object AnalyzeCombinedByHour(List<TicketItem> tickets)
        {
            var groups = tickets.GroupBy(t => t.CreatedHour).OrderBy(g => g.Key).Select(g => new
                {
                    Hour = g.Key.ToString("00") + ":00",
                    Count = g.Count()
                }).ToList();

            return new
            {
                type = "combined_hour", labels = groups.Select(g => g.Hour).ToArray(), datasets = new[]
                {
                    new
                    {
                        label = "Количество заявок по часам",
                        data = groups.Select(g => g.Count).ToArray()
                    }
                }
            };
        }

        static object AnalyzeCombinedByDay(List<TicketItem> tickets)
        {
            var groups = tickets.GroupBy(t => t.CreatedDate.Date).OrderBy(g => g.Key).Select(g => new
                {
                    Date = g.Key.ToString("dd.MM.yyyy"),
                    Count = g.Count()
                }).ToList();

            return new
            {
                type = "combined_day",
                labels = groups.Select(g => g.Date).ToArray(), datasets = new[]
                {
                    new
                    {
                        label = "Количество заявок по дням",
                        data = groups.Select(g => g.Count).ToArray()
                    }
                }
            };
        }

        static object AnalyzeCombinedByWeek(List<TicketItem> tickets)
        {
            var groups = tickets.GroupBy(t => GetWeekStart(t.CreatedDate)).OrderBy(g => g.Key).Select(g => new
                {
                    Week = g.Key.ToString("dd.MM.yyyy"),
                    Count = g.Count()
                }).ToList();

            return new
            {
                type = "combined_week",
                labels = groups.Select(g => g.Week).ToArray(),
                datasets = new[]
                {
                    new
                    {
                        label = "Количество заявок по неделям",
                        data = groups.Select(g => g.Count).ToArray()
                    }
                }
            };
        }

        static object AnalyzeCombinedByMonth(List<TicketItem> tickets)
        {
            var groups = tickets.GroupBy(t => new DateTime(t.CreatedDate.Year, t.CreatedDate.Month, 1)).OrderBy(g => g.Key).Select(g => new
                {
                    Month = g.Key.ToString("MM.yyyy"),
                    Count = g.Count()
                }).ToList();

            return new
            {
                type = "combined_month",
                labels = groups.Select(g => g.Month).ToArray(), datasets = new[]
                {
                    new
                    {
                        label = "Количество заявок по месяцам",
                        data = groups.Select(g => g.Count).ToArray()
                    }
                }
            };
        }

        static object AnalyzeStatuses(List<TicketItem> tickets)
        {
            var groups = tickets.Where(t => !string.IsNullOrWhiteSpace(t.Status)).GroupBy(t => t.Status).OrderBy(g => g.Key).Select(g => new
                {Status = g.Key, Count = g.Count()}).ToList();

            return new
            {
                type = "statuses",
                labels = groups.Select(g => g.Status).ToArray(), datasets = new[] 
                { 
                    new
                    {
                        label = "Количество заявок по статусам",
                        data = groups.Select(g => g.Count).ToArray()
                    }
                }
            };
        }

        static object AnalyzeLines(List<TicketItem> tickets)
        {
            var groups = tickets.Where(t => !string.IsNullOrWhiteSpace(t.Line)).GroupBy(t => t.Line).OrderBy(g => g.Key).Select(g => new
            {Line = g.Key,Count = g.Count()}).ToList();

            return new
            {
                type = "lines",
                labels = groups.Select(g => g.Line).ToArray(),
                datasets = new[]
                {
                    new
                    {
                        label = "Количество заявок по линиям",
                        data = groups.Select(g => g.Count).ToArray()
                    }
                }
            };
        }

        static DateTime GetWeekStart(DateTime date)
        {
            int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.Date.AddDays(-diff);
        }
    }
}
