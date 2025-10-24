using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using System.Net;

namespace nginx_proxy_manager_management_Interface.Services
{
    public class GeoIpService
    {
        private readonly string _geoIpDbPath;
        private readonly ILogger<GeoIpService> _logger;

        public GeoIpService(IConfiguration configuration, ILogger<GeoIpService> logger)
        {
            // Path matches your Docker volume mount
            _geoIpDbPath = configuration["GeoIP:DatabasePath"] ??
                          Path.Combine("wwwroot", "host_data", "GeoLite2-City.mmdb");
            _logger = logger;
        }

        public GeoIpResult? LookupIp(string ipAddress)
        {
            try
            {
                if (!File.Exists(_geoIpDbPath))
                {
                    _logger.LogWarning($"GeoIP database not found at {_geoIpDbPath}");
                    return null;
                }

                using var reader = new DatabaseReader(_geoIpDbPath);

                if (!IPAddress.TryParse(ipAddress, out var ip))
                {
                    _logger.LogWarning($"Invalid IP address: {ipAddress}");
                    return null;
                }

                var response = reader.City(ip);

                return new GeoIpResult
                {
                    Success = true,
                    IpAddress = ipAddress,
                    City = response.City.Name ?? "Unknown",
                    Country = response.Country.Name ?? "Unknown",
                    CountryCode = response.Country.IsoCode ?? "XX",
                    Latitude = response.Location.Latitude ?? 0,
                    Longitude = response.Location.Longitude ?? 0
                };
            }
            catch (AddressNotFoundException)
            {
                _logger.LogInformation($"IP address not found in database: {ipAddress}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error looking up IP: {ipAddress}");
                return null;
            }
        }
    }

    public class GeoIpResult
    {
        public bool Success { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
