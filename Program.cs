using BackendAcctTask.Settings;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// MONGODB SETTINGS
// ============================================================
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDbSettings")
);

var mongoConnection =
    builder.Configuration["MongoDbSettings:ConnectionString"];

var mongoDatabase =
    builder.Configuration["MongoDbSettings:DatabaseName"];

if (string.IsNullOrWhiteSpace(mongoConnection))
{
    throw new InvalidOperationException(
        "MongoDbSettings:ConnectionString is not configured."
    );
}

if (string.IsNullOrWhiteSpace(mongoDatabase))
{
    throw new InvalidOperationException(
        "MongoDbSettings:DatabaseName is not configured."
    );
}

var mongoClient = new MongoClient(mongoConnection);

var database = mongoClient.GetDatabase(mongoDatabase);

// IMongoDatabase is registered so BaseController can inject it directly
builder.Services.AddSingleton<IMongoDatabase>(database);


// ============================================================
// CONTROLLERS
// ============================================================

builder.Services.AddControllers();

// NOTE: Service registrations (AccountTypeService, ChartAccountService, etc.) 
// have been removed because you are now using static DB helper classes 
// and BaseController with direct IMongoDatabase injection.


// ============================================================
// CORS
// ============================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


// ============================================================
// SWAGGER
// ============================================================

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// ============================================================
// BUILD
// ============================================================

var app = builder.Build();


// ============================================================
// MIDDLEWARE
// ============================================================

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("Frontend");

// app.UseHttpsRedirection();

app.UseAuthorization();

app.UseStaticFiles();

app.MapControllers();

app.Run();