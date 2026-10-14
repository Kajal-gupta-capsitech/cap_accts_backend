using BackendAcctTask.Data.MongoDB;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// MONGODB CONFIGURATION (Single Source of Truth)
// ============================================================
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

var dbConfiguration = new DBConfiguration(mongoConnection, mongoDatabase);
builder.Services.AddSingleton(dbConfiguration);


// ============================================================
// CONTROLLERS
// ============================================================

builder.Services.AddControllers();

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