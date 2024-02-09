
// check if service is already installed
for (var port = 5095; port <= 5098; port++)
    using (var httpClient = new HttpClient())
    {
        try
        {
            var response = await httpClient.GetAsync($"http://localhost:{port}/api/checkService");
            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                if (responseBody == "HotReloadKit service is working") return;
            }
        }
        catch { }
    }

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// run HotReloadKit service
for (var port = 5095; port <= 5098; port++)
{
    try
    {        
        app.Run($"http://localhost:{port}");
    }
    catch { }
}   
