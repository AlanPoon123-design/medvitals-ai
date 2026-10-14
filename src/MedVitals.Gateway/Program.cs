using MedVitals.Gateway.Services;

var builder = WebApplication.CreateBuilder(args);

// services to the container.
builder.Services.AddControllers();
builder.Services.AddHttpClient<AnalyticsService>();

// learn more about configuring Swagger/OpenAI https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// configure HTTp request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();