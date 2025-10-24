using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using nginx_proxy_manager_management_Interface.Services;
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
            var allRules = JsonHandler.DeserializeJsonBuiltIn<Dictionary<string, IpRule>>(ipListPath)
                         .Values
                         .ToList();

            int DefaultPageSize = MaxIPsPerPage;

            // Pagination calculations
            int pageSize = DefaultPageSize;
            CurrentPage = 1;
            TotalPages = (int)Math.Ceiling(allRules.Count / (double)pageSize);

            IpRules = allRules.Skip((CurrentPage - 1) * pageSize).Take(pageSize).ToList();
            IpRulesFull = allRules;
        }

        public JsonResult OnGetIpRulesPageJson([FromQuery] int page = 1, [FromQuery] string filter = "all")
        {
            Console.WriteLine($"Fetching Page {page}");

            var allRules = JsonHandler.DeserializeJsonBuiltIn<Dictionary<string, IpRule>>(ipListPath)
                         .Values
                         .ToList();

            int pageSize = _configuration.GetValue<int>("ServiceVariables:MaxIPsPerPage");
            CurrentPage = page;
            TotalPages = (int)Math.Ceiling(allRules.Count / (double)pageSize);

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
            if (SaveIpRule(ipAddress, action, "manual"))
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
            Dictionary<string, IpRule> existingRules = JsonHandler.DeserializeJsonBuiltIn<Dictionary<string, IpRule>>(ipListPath);
            if (existingRules.ContainsKey(ipAddress))
            {
                IpRule tempRule = existingRules[ipAddress];
                DeleteIpRule(ipAddress);
                SaveIpRule(ipAddress, "allow", tempRule.addType);
                existingRules[ipAddress].DateAdded = tempRule.DateAdded;
                existingRules[ipAddress].Action = "allow";
                JsonHandler.SerializeJsonFile(ipListPath, existingRules);
                return RedirectToPage();
            }
            else
            {
                throw new Exception("IP rule not found");
            }
        }

        public IActionResult OnPostBlockRule(string ipAddress)
        {
            Dictionary<string, IpRule> existingRules = JsonHandler.DeserializeJsonBuiltIn<Dictionary<string, IpRule>>(ipListPath);
            if (existingRules.ContainsKey(ipAddress))
            {
                IpRule tempRule = existingRules[ipAddress];
                DeleteIpRule(ipAddress);
                SaveIpRule(ipAddress, "block", tempRule.addType);
                existingRules[ipAddress].DateAdded = tempRule.DateAdded;
                existingRules[ipAddress].Action = "block";
                JsonHandler.SerializeJsonFile(ipListPath, existingRules);
                return RedirectToPage();
            }
            else
            {
                throw new Exception("IP rule not found");
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

        private bool SaveIpRule(string ip, string action, string addType)
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
                    DateAdded = DateTime.Now,
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

        private bool DeleteIpRule(string ip)
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