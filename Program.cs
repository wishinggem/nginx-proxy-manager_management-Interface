using Newtonsoft.Json;
using System.Text.Json;

namespace nginx_proxy_manager_management_Interface
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorPages();
            // Add this where you register other services (before builder.Build())
            builder.Services.AddSingleton<nginx_proxy_manager_management_Interface.Services.GeoIpService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapRazorPages();

            app.Run();
        }
    }
}

public static class JsonHandler
{
    public static void SerializeJsonFile<T>(string filePath, T obj, bool append = false)
    {
        using var writer = new StreamWriter(filePath, append);
        writer.Write(JsonConvert.SerializeObject(obj));
    }

    public static T DeserializeJsonBuiltIn<T>(string filePath) where T : new()
    {
        if (!System.IO.File.Exists(filePath))
            return new T();

        using var reader = new StreamReader(filePath);
        return JsonConvert.DeserializeObject<T>(reader.ReadToEnd());
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task SerializeJsonFileBuiltIn<T>(string filePath, T obj, bool append = false)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(obj, Options);
        await File.WriteAllTextAsync(filePath, json);
    }

    public static async Task<T> DeserializeJsonFileBuiltIn<T>(string filePath) where T : new()
    {
        if (!File.Exists(filePath))
            return new T();

        var json = await File.ReadAllTextAsync(filePath);
        return System.Text.Json.JsonSerializer.Deserialize<T>(json, Options) ?? new T();
    }
}