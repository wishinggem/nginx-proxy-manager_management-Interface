using Azure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using nginx_proxy_manager_management_Interface.Services;
using System.Data.Common;
using System.Net;
using System.Text.Json;

namespace nginx_proxy_manager_management_Interface.Pages
{
    public class IndexModel : PageModel
    {
        public List<IpRule> IpRules { get; set; } = new List<IpRule>();
        public List<IpRule> IpRulesFull { get; set; } = new List<IpRule>();
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; }
        private readonly ILogger<IndexModel> _logger;
        private readonly IConfiguration _configuration;

        public readonly string ipListPath = Path.Combine("wwwroot", "host_data", "ips.json");
        public readonly string ipReaderFilePath = Path.Combine("wwwroot", "host_data", "ips.conf");

        private void EnsureConfigFilesExist()
        {
            if (!System.IO.File.Exists(ipReaderFilePath))
            {
                var file = System.IO.File.Create(ipReaderFilePath);
                file.Close();
                Thread.Sleep(2);
            }
            if (!System.IO.File.Exists(ipListPath))
            {
                Dictionary<string, IpRule> emptyList = new Dictionary<string, IpRule>(); //ip address - iprule
                JsonHandler.SerializeJsonFile(ipListPath, emptyList);
                Thread.Sleep(2);
            }
        }

        public IndexModel(ILogger<IndexModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public void OnGet()
        {
            //get service vars

            int IPMarkTreadLimit = _configuration.GetValue<int>("ServiceVariables:IPMapMarkerLoadingThreadCount");
            bool EnableMaliciousMapMarkers = _configuration.GetValue<bool>("ServiceVariables:EnableMaliciousMapMarkers");
            bool EnableMapDynamicTimeout = _configuration.GetValue<bool>("ServiceVariables:EnableMapDynamicTimeout");
            int MapDynamicTimeout = _configuration.GetValue<int>("ServiceVariables:MapLoadingTimeout");
            int IPIndexingBatchSize = _configuration.GetValue<int>("ServiceVariables:IPIndexingBatchSize");
            int MaxIPsPerPage = _configuration.GetValue<int>("ServiceVariables:MaxIPsPerPage");

            ViewData["IPMarkTreadLimit"] = IPMarkTreadLimit;
            ViewData["EnableMaliciousMapMarkers"] = EnableMaliciousMapMarkers;
            ViewData["EnableMapDynamicTimeout"] = EnableMapDynamicTimeout;
            ViewData["MapDynamicTimeout"] = MapDynamicTimeout;
            ViewData["IPIndexingBatchSize"] = IPIndexingBatchSize;

            EnsureConfigFilesExist();

            var useDB = _configuration.GetValue<bool>("ServiceVariables:useDB");
            var dbIP = _configuration.GetValue<string>("ServiceVariables:dbIP");
            var dbUser = _configuration.GetValue<string>("ServiceVariables:dbUser");
            var dbPassword = _configuration.GetValue<string>("ServiceVariables:dbPassword");
            var dbTable = _configuration.GetValue<string>("ServiceVariables:dbTable");
            var dbName = _configuration.GetValue<string>("ServiceVariables:dbName");

            if (useDB)
            {
                Console.WriteLine("Loading IP rules from database...");

                string connectionString = $"Server={dbIP};Database={dbName};User Id={dbUser};Password={dbPassword};TrustServerCertificate=True;";

                using (SqlConnection conn = new(connectionString))
                {
                    conn.Open();

                    // Load all rules
                    string query = $"SELECT * FROM {dbTable} order by CASE WHEN addType = 'manual' THEN 0 ELSE 1 END, addType";

                    using (SqlCommand cmd = new(query, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            IpRulesFull.Add(new IpRule
                            {
                                Id = reader.GetInt32(0),
                                IpAddress = reader.GetString(1),
                                Action = reader.GetString(2),
                                DateAdded = reader.GetDateTime(3),
                                addType = reader.GetString(4)
                            });
                        }

                        int pageSize = _configuration.GetValue<int>("ServiceVariables:MaxIPsPerPage");
                        CurrentPage = 1;
                        TotalPages = (int)Math.Ceiling(IpRulesFull.Count / (double)pageSize);
                    }
                }
            }
            else
            {
                Console.WriteLine("Loading IP rules from JSON file...");

                CurrentPage = 1;
                Console.WriteLine($"Fetching Page {CurrentPage}");

                IpRulesFull = JsonHandler.DeserializeJsonBuiltIn<Dictionary<string, IpRule>>(ipListPath)
                             .Values
                             .ToList();

                int pageSize = _configuration.GetValue<int>("ServiceVariables:MaxIPsPerPage");
                
                TotalPages = (int)Math.Ceiling(IpRulesFull.Count / (double)pageSize);
            }
        }

        public JsonResult OnGetIpRulesPageJson([FromQuery] int page = 1, [FromQuery] string filter = "all")
        {
            var useDB = _configuration.GetValue<bool>("ServiceVariables:useDB");
            var dbIP = _configuration.GetValue<string>("ServiceVariables:dbIP");
            var dbUser = _configuration.GetValue<string>("ServiceVariables:dbUser");
            var dbPassword = _configuration.GetValue<string>("ServiceVariables:dbPassword");
            var dbTable = _configuration.GetValue<string>("ServiceVariables:dbTable");
            var dbName = _configuration.GetValue<string>("ServiceVariables:dbName");

            Console.WriteLine($"Fetching Page {page}");

            if (useDB)
            {
                Console.WriteLine("Loading IP rules from database...");

                string connectionString = $"Server={dbIP};Database={dbName};User Id={dbUser};Password={dbPassword};TrustServerCertificate=True;";

                List<IpRule> allRules = new();

                using (SqlConnection conn = new(connectionString))
                {
                    conn.Open();

                    // Load all rules
                    string query = $"SELECT * FROM {dbTable} order by CASE WHEN addType = 'manual' THEN 0 ELSE 1 END, addType";

                    using (SqlCommand cmd = new(query, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            allRules.Add(new IpRule
                            {
                                Id = reader.GetInt32(0),
                                IpAddress = reader.GetString(1),
                                Action = reader.GetString(2),
                                DateAdded = reader.GetDateTime(3),
                                addType = reader.GetString(4)
                            });
                        }

                        int pageSize = _configuration.GetValue<int>("ServiceVariables:MaxIPsPerPage");
                        CurrentPage = page;
                        TotalPages = (int)Math.Ceiling(allRules.Count / (double)pageSize);

                        IpRulesFull = allRules;

                        var rulesPage = allRules.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        var filteredRules = allRules;

                        if (filter != "all")
                        {
                            filteredRules = allRules.Where(r => r.addType == filter).ToList();
                            rulesPage = filteredRules.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                            TotalPages = (int)Math.Ceiling(filteredRules.Count / (decimal)pageSize);
                        }

                        Console.WriteLine($"Applying filter: {filter}");
                        Console.WriteLine($"IP Count: {filteredRules.Count}");
                        Console.WriteLine($"Total Pages: {TotalPages}");

                        var jsonOptions = new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        };

                        return new JsonResult(new { rules = rulesPage, totalPages = TotalPages }, jsonOptions);
                    }
                }
            }
            else
            {
                Console.WriteLine("Loading IP rules from JSON file...");

                var allRules = JsonHandler.DeserializeJsonBuiltIn<Dictionary<string, IpRule>>(ipListPath)
                             .Values
                             .ToList();

                int pageSize = _configuration.GetValue<int>("ServiceVariables:MaxIPsPerPage");
                CurrentPage = page;
                TotalPages = (int)Math.Ceiling(allRules.Count / (double)pageSize);

                IpRulesFull = allRules;

                var rulesPage = allRules.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                var filteredRules = allRules;

                if (filter != "all")
                {
                    filteredRules = allRules.Where(r => r.addType == filter).ToList();
                    rulesPage = filteredRules.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                    TotalPages = (int)Math.Ceiling(filteredRules.Count / (decimal)pageSize);
                }

                Console.WriteLine($"Applying filter: {filter}");
                Console.WriteLine($"IP Count: {filteredRules.Count}");
                Console.WriteLine($"Total Pages: {TotalPages}");

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                return new JsonResult(new { rules = rulesPage, totalPages = TotalPages }, jsonOptions);
            }
        }



        public IActionResult OnPostAddRule(string ipAddress, string action)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                TempData["ErrorMessage"] = "IP address is required";
                return RedirectToPage();
            }

            if (string.IsNullOrWhiteSpace(action))
            {
                TempData["ErrorMessage"] = "Action is required";
                return RedirectToPage();
            }

            // TODO: Validate IP address format
            if (!ValidateIpAddress(ipAddress))
            {
                TempData["ErrorMessage"] = "Invalid IP address format";
                return RedirectToPage();
            }

            // TODO: Check if IP already exists
            // TODO: Save new rule to database or configuration
            if (SaveIpRule(ipAddress, action, "manual", DateTime.Now))
            {
                TempData["SuccessMessage"] = "IP rule added successfully";
            }
            else
            {

            }

            return RedirectToPage();
        }

        public IActionResult OnPostDeleteRule(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                TempData["ErrorMessage"] = "IP address is required";
                return RedirectToPage();
            }

            // TODO: Find and delete the rule from database or configuration
            if (DeleteIpRule(ipAddress))
            {
                TempData["SuccessMessage"] = "IP rule removed successfully";
            }
            else
            {
                
            }

            return RedirectToPage();
        }

        public IActionResult OnPostAllowRule(string ipAddress)
        {
            var useDB = _configuration.GetValue<bool>("ServiceVariables:useDB");
            try
            {
                if (useDB)
                {
                    if (DoesIpExistSql(ipAddress))
                    {
                        IpRule tempRule = GetIpRuleSql(ipAddress);
                        DeleteIpRule(ipAddress);
                        SaveIpRule(ipAddress, "allow", tempRule.addType, tempRule.DateAdded);
                        return RedirectToPage();
                    }
                    else
                    {
                        throw new Exception("IP rule not found");
                    }
                }
                else
                {
                    Dictionary<string, IpRule> existingRules = JsonHandler.DeserializeJsonBuiltIn<Dictionary<string, IpRule>>(ipListPath);
                    if (existingRules.ContainsKey(ipAddress))
                    {
                        IpRule tempRule = existingRules[ipAddress];
                        DeleteIpRule(ipAddress);
                        SaveIpRule(ipAddress, "allow", tempRule.addType, tempRule.DateAdded);
                        JsonHandler.SerializeJsonFile(ipListPath, existingRules);
                        return RedirectToPage();
                    }
                    else
                    {
                        throw new Exception("IP rule not found");
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }
        }

        public IActionResult OnPostBlockRule(string ipAddress)
        {
            var useDB = _configuration.GetValue<bool>("ServiceVariables:useDB");
            try
            {
                if (useDB)
                {
                    if (DoesIpExistSql(ipAddress))
                    {
                        IpRule tempRule = GetIpRuleSql(ipAddress);
                        DeleteIpRule(ipAddress);
                        SaveIpRule(ipAddress, "block", tempRule.addType, tempRule.DateAdded);
                        return RedirectToPage();
                    }
                    else
                    {
                        throw new Exception("IP rule not found");
                    }
                }
                else
                {
                    Dictionary<string, IpRule> existingRules = JsonHandler.DeserializeJsonBuiltIn<Dictionary<string, IpRule>>(ipListPath);
                    if (existingRules.ContainsKey(ipAddress))
                    {
                        IpRule tempRule = existingRules[ipAddress];
                        DeleteIpRule(ipAddress);
                        SaveIpRule(ipAddress, "block", tempRule.addType, tempRule.DateAdded);
                        JsonHandler.SerializeJsonFile(ipListPath, existingRules);
                        return RedirectToPage();
                    }
                    else
                    {
                        throw new Exception("IP rule not found");
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }
        }

        private bool DoesIpExistSql(string ip)
        {
            var dbIP = _configuration.GetValue<string>("ServiceVariables:dbIP");
            var dbUser = _configuration.GetValue<string>("ServiceVariables:dbUser");
            var dbPassword = _configuration.GetValue<string>("ServiceVariables:dbPassword");
            var dbTable = _configuration.GetValue<string>("ServiceVariables:dbTable");
            var dbName = _configuration.GetValue<string>("ServiceVariables:dbName");

            string connectionString = $"Server={dbIP};Database={dbName};User Id={dbUser};Password={dbPassword};TrustServerCertificate=True;";
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                Console.WriteLine("Database connection opened successfully");

                // Check if IP exists
                string checkQuery = "SELECT COUNT(*) FROM " + dbTable + " WHERE IP = @IpAddress";
                using (var checkCmd = new SqlCommand(checkQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@IpAddress", ip);
                    int count = (int)checkCmd.ExecuteScalar();
                    Console.WriteLine($"Found {count} record(s) for IP: {ip}");

                    if (count == 0)
                    {
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
            }
        }

        private IpRule GetIpRuleSql(string ip)
        {
            var dbIP = _configuration.GetValue<string>("ServiceVariables:dbIP");
            var dbUser = _configuration.GetValue<string>("ServiceVariables:dbUser");
            var dbPassword = _configuration.GetValue<string>("ServiceVariables:dbPassword");
            var dbTable = _configuration.GetValue<string>("ServiceVariables:dbTable");
            var dbName = _configuration.GetValue<string>("ServiceVariables:dbName");

            try
            {
                string connectionString = $"Server={dbIP};Database={dbName};User Id={dbUser};Password={dbPassword};TrustServerCertificate=True;";
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    string query = "SELECT ID, IP, Action, DateAdded, addType FROM " + dbTable + " WHERE IP = @IpAddress";
                    using (var cmd = new SqlCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@IpAddress", ip);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return new IpRule
                                {
                                    Id = reader.GetInt32(0),
                                    IpAddress = reader.GetString(1),
                                    Action = reader.GetString(2),
                                    DateAdded = reader.GetDateTime(3),
                                    addType = reader.GetString(4)
                                };
                            }
                            else
                            {
                                return null; // IP not found
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error getting IP rule: {e.Message}");
                return null;
            }
        }

        private bool ValidateIpAddress(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            string[] parts = ip.Split('.');
            if (parts.Length != 4)
                return false;

            foreach (string part in parts)
            {
                if (!int.TryParse(part, out int value) || value < 0 || value > 255)
                    return false;
            }

            return true;
        }

        private bool SaveIpRule(string ip, string action, string addType, DateTime dateAdded)
        {
            var useDB = _configuration.GetValue<bool>("ServiceVariables:useDB");
            var dbIP = _configuration.GetValue<string>("ServiceVariables:dbIP");
            var dbUser = _configuration.GetValue<string>("ServiceVariables:dbUser");
            var dbPassword = _configuration.GetValue<string>("ServiceVariables:dbPassword");
            var dbTable = _configuration.GetValue<string>("ServiceVariables:dbTable");
            var dbName = _configuration.GetValue<string>("ServiceVariables:dbName");

            if (useDB)
            {
                try
                {
                    string connectionString = $"Server={dbIP};Database={dbName};User Id={dbUser};Password={dbPassword};TrustServerCertificate=True;";

                    using (var connection = new SqlConnection(connectionString))
                    {
                        connection.Open();

                        // Check if IP already exists
                        string checkQuery = "SELECT COUNT(*) FROM " + dbTable + " WHERE IP = @IpAddress";
                        using (var checkCmd = new SqlCommand(checkQuery, connection))
                        {
                            checkCmd.Parameters.AddWithValue("@IpAddress", ip);
                            int count = (int)checkCmd.ExecuteScalar();

                            if (count > 0)
                            {
                                throw new Exception("IP rule already exists");
                            }
                        }

                        // Get the next Id (count of existing records + 1)
                        string countQuery = "SELECT COUNT(*) FROM " + dbTable;
                        int nextId;
                        using (var countCmd = new SqlCommand(countQuery, connection))
                        {
                            nextId = (int)countCmd.ExecuteScalar() + 1;
                        }

                        // Insert new rule
                        string insertQuery = @"INSERT INTO " + dbTable + @" (ID, IP, Action, DateAdded, addType) 
                              VALUES (@Id, @IpAddress, @Action, @DateAdded, @addType)";
                        using (var insertCmd = new SqlCommand(insertQuery, connection))
                        {
                            insertCmd.Parameters.AddWithValue("@Id", nextId);
                            insertCmd.Parameters.AddWithValue("@IpAddress", ip);
                            insertCmd.Parameters.AddWithValue("@Action", action);
                            insertCmd.Parameters.AddWithValue("@DateAdded", dateAdded);
                            insertCmd.Parameters.AddWithValue("@addType", addType);

                            insertCmd.ExecuteNonQuery();
                        }
                    }

                    UpdateIpReaderFile(ip, action);
                    return true;
                }
                catch (Exception e)
                {
                    TempData["ErrorMessage"] = e.Message;
                    return false;
                }
            }
            else
            {
                try
                {
                    Dictionary<string, IpRule> existingRules = JsonHandler.DeserializeJsonBuiltIn<Dictionary<string, IpRule>>(ipListPath);

                    if (existingRules.ContainsKey(ip))
                    {
                        throw new Exception("IP rule already exists");
                    }

                    IpRule newRule = new IpRule
                    {
                        Id = existingRules.Keys.Count + 1,
                        IpAddress = ip,
                        Action = action,
                        DateAdded = dateAdded,
                        addType = addType
                    };
                    existingRules.Add(newRule.IpAddress, newRule);
                    JsonHandler.SerializeJsonFile(ipListPath, existingRules);
                    UpdateIpReaderFile(ip, action);
                    return true;
                }
                catch (Exception e)
                {
                    TempData["ErrorMessage"] = e.Message;
                    return false;
                }
            }
        }

        private bool DeleteIpRule(string ip)
        {
            var useDB = _configuration.GetValue<bool>("ServiceVariables:useDB");
            var dbIP = _configuration.GetValue<string>("ServiceVariables:dbIP");
            var dbUser = _configuration.GetValue<string>("ServiceVariables:dbUser");
            var dbPassword = _configuration.GetValue<string>("ServiceVariables:dbPassword");
            var dbTable = _configuration.GetValue<string>("ServiceVariables:dbTable");
            var dbName = _configuration.GetValue<string>("ServiceVariables:dbName");

            if (useDB)
            {
                try
                {
                    Console.WriteLine($"Attempting to delete IP: {ip}");
                    Console.WriteLine($"Using database: {dbName}, table: {dbTable}");

                    string connectionString = $"Server={dbIP};Database={dbName};User Id={dbUser};Password={dbPassword};TrustServerCertificate=True;";
                    using (var connection = new SqlConnection(connectionString))
                    {
                        connection.Open();
                        Console.WriteLine("Database connection opened successfully");

                        // Check if IP exists
                        string checkQuery = "SELECT COUNT(*) FROM " + dbTable + " WHERE IP = @IpAddress";
                        using (var checkCmd = new SqlCommand(checkQuery, connection))
                        {
                            checkCmd.Parameters.AddWithValue("@IpAddress", ip);
                            int count = (int)checkCmd.ExecuteScalar();
                            Console.WriteLine($"Found {count} record(s) for IP: {ip}");

                            if (count == 0)
                            {
                                throw new Exception("IP rule not found");
                            }
                        }

                        Console.WriteLine($"Removing IP: {ip}");

                        // Delete the IP rule
                        string deleteQuery = "DELETE FROM " + dbTable + " WHERE IP = @IpAddress";
                        using (var deleteCmd = new SqlCommand(deleteQuery, connection))
                        {
                            deleteCmd.Parameters.AddWithValue("@IpAddress", ip);
                            int rowsAffected = deleteCmd.ExecuteNonQuery();
                            Console.WriteLine($"Rows deleted: {rowsAffected}");
                        }
                    }

                    Console.WriteLine("Calling RemoveIpFromReaderFile");
                    RemoveIpFromReaderFile(ip);
                    Console.WriteLine("Delete operation completed successfully");
                    return true;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error during delete: {e.Message}");
                    Console.WriteLine($"Stack trace: {e.StackTrace}");
                    TempData["ErrorMessage"] = e.Message;
                    return false;
                }
            }
            else
            {
                try
                {
                    Dictionary<string, IpRule> existingRules = JsonHandler.DeserializeJsonBuiltIn<Dictionary<string, IpRule>>(ipListPath);
                    if (existingRules.ContainsKey(ip))
                    {
                        Console.WriteLine($"Removing IP: {ip}");
                        existingRules.Remove(ip);
                        RemoveIpFromReaderFile(ip);
                        JsonHandler.SerializeJsonFile(ipListPath, existingRules);
                        return true;
                    }
                    else
                    {
                        throw new Exception("IP rule not found");
                    }
                }
                catch (Exception e)
                {
                    TempData["ErrorMessage"] = e.Message;
                    return false;
                }
            }
        }

        private void UpdateIpReaderFile(string ip, string action)
        {
            if (action == "block")
            {
                action = "deny";
            }
            else
            {
                action = "allow";
            }
            System.IO.File.AppendAllText(ipReaderFilePath, $"{action} {ip};" + Environment.NewLine);
        }

        private bool RemoveIpFromReaderFile(string ip)
        {
            string filename = ipReaderFilePath;

            try
            {
                if (!System.IO.File.Exists(filename))
                {
                    return false;
                }

                var lines = System.IO.File.ReadAllLines(filename);
                var filteredLines = lines.Where(line => !line.Contains(ip)).ToArray();

                if (lines.Length == filteredLines.Length)
                {
                    return false; // IP not found in file
                }

                System.IO.File.WriteAllLines(filename, filteredLines);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public IActionResult OnGetGeoIp(string ipAddress)
        {
            try
            {
                var geoIpService = HttpContext.RequestServices.GetRequiredService<GeoIpService>();
                var result = geoIpService.LookupIp(ipAddress);

                if (result == null)
                {
                    return new JsonResult(new { success = false, message = "IP not found in local database" });
                }

                return new JsonResult(new
                {
                    success = result.Success,
                    ipAddress = result.IpAddress,
                    city = result.City,
                    country = result.Country,
                    countryCode = result.CountryCode,
                    latitude = result.Latitude,
                    longitude = result.Longitude
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in GeoIP lookup for {ipAddress}");
                return new JsonResult(new { success = false, message = "Error looking up IP" });
            }
        }

        public IActionResult OnPostClearAll(string filterType)
        {
            try
            {
                Dictionary<string, IpRule> existingRules = JsonHandler.DeserializeJsonBuiltIn<Dictionary<string, IpRule>>(ipListPath);
                var ipsToRemove = existingRules.Values
                    .Where(rule => rule.addType == filterType)
                    .Select(rule => rule.IpAddress)
                    .ToList();
                foreach (var ip in ipsToRemove)
                {
                    DeleteIpRule(ip);
                }
                TempData["SuccessMessage"] = "Filtered IP rules cleared successfully";
                return RedirectToPage();
            }
            catch (Exception e)
            {
                TempData["ErrorMessage"] = e.Message;
                return RedirectToPage();
            }
        }
    }

    public class IpRule
    {
        public int Id { get; set; }
        public string IpAddress { get; set; }
        public string Action { get; set; } // "allow" or "block"
        public DateTime DateAdded { get; set; }
        public string addType { get; set; } // "manual" or "automatic" or "malicious"
    }
}