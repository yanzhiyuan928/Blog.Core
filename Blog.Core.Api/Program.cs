
// 以下为asp.net 6.0的写法，如果用5.0，请看Program.five.cs文件
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Blog.Core;
using Blog.Core.Common;
using Blog.Core.Common.LogHelper;
using Blog.Core.Common.Seed;
using Blog.Core.Extensions;
using Blog.Core.Extensions.Apollo;
using Blog.Core.Extensions.Middlewares;
using Blog.Core.Filter;
using Blog.Core.Hubs;
using Blog.Core.IServices;
using Blog.Core.Tasks;
using FluentValidation.AspNetCore;// Model 验证
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1、配置host与容器
builder.Host
.UseServiceProviderFactory(new AutofacServiceProviderFactory())
.ConfigureContainer<ContainerBuilder>(builder =>
{
    // 服务注册
    builder.RegisterModule(new AutofacModuleRegister());
    builder.RegisterModule<AutofacPropertityModuleReg>();
})
.ConfigureLogging((hostingContext, builder) =>
{
    // 过滤掉系统默认的一些日志，也可以配置在appsettings.json --LogLevel节点
    builder.AddFilter("System", LogLevel.Error);
    builder.AddFilter("Microsoft", LogLevel.Error);
    // 同一设置日志级别
    builder.SetMinimumLevel(LogLevel.Error);
    builder.AddLog4Net(Path.Combine(Directory.GetCurrentDirectory(), "Log4net.config"));
})
.ConfigureAppConfiguration((hostingContext, config) =>
{
    // 配置文件设置
    config.Sources.Clear();
    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
    config.AddConfigurationApollo("appsettings.apollo.json");
});


// 2、配置服务
builder.Services.AddSingleton(new Appsettings(builder.Configuration));
builder.Services.AddSingleton(new LogLock(builder.Environment.ContentRootPath));
builder.Services.AddUiFilesZipSetup(builder.Environment);

Permissions.IsUseIds4 = Appsettings.app(new string[] { "Startup", "IdentityServer4", "Enabled" }).ObjToBool();
RoutePrefix.Name = Appsettings.app(new string[] { "AppSettings", "SvcName" }).ObjToString();

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

// 缓存 Cache
builder.Services.AddMemoryCacheSetup();
// Redis 
builder.Services.AddRedisCacheSetup();
// SqlSugar 服务(连接，从库) 相关配置
builder.Services.AddSqlsugarSetup();
// Db 启动服务-- DBSeed： 数据库、表创建； MyContext
builder.Services.AddDbSetup();
// Automapper 实体映射关系
builder.Services.AddAutoMapperSetup();
// Cors 跨域设置
builder.Services.AddCorsSetup();
// MiniProfiler 监视
builder.Services.AddMiniProfilerSetup();
// Swagger 配置
builder.Services.AddSwaggerSetup();
// Quartz Job 任务配置
builder.Services.AddJobSetup();
// HttpContext 相关参数
builder.Services.AddHttpContextSetup();
// 控制台输出相关配置信息
builder.Services.AddAppTableConfigSetup(builder.Environment);
// 外网API接口
builder.Services.AddHttpApi();
// Redis消息发布/订阅
builder.Services.AddRedisInitMqSetup();
// RabbitMQ持久连接 
builder.Services.AddRabbitMQSetup();
// 注入Kafka相关配置
builder.Services.AddKafkaSetup(builder.Configuration);
//EventBus 事件总线服务 (RabbitMQ、Kafka )
builder.Services.AddEventBusSetup();
//Nacos 配置相关
builder.Services.AddNacosSetup(builder.Configuration);
// 系统 授权服务 配置
builder.Services.AddAuthorizationSetup();
if (Permissions.IsUseIds4)
{
    // Ids4权限 认证服务
    builder.Services.AddAuthentication_Ids4Setup();
}
else
{
    // JWT权限 认证服务
    builder.Services.AddAuthentication_JWTSetup();
}
// IPLimit限流
builder.Services.AddIpPolicyRateLimitSetup(builder.Configuration);

builder.Services.AddSignalR().AddNewtonsoftJsonProtocol();
// ActionFilterAttribute 过滤器
builder.Services.AddScoped<UseServiceDIAttribute>();
// Kestrel 是嵌入在 asp. net Core 应用程序中的跨平台 web 服务器 使用进程外(out-of-Process)托管
// 示意图 ： internet  <---> Kestrel 包含 (dotnet.exe 包含 App)

// 使用InProcess托管，应用程序托管在IIS工作进程（w3wp.exe或iisexpress.exe）中，使用进程内托管
// 示意图 ： internet  <---> IIS 包含 (w3wp.exe 包含 App)
builder.Services.Configure<KestrelServerOptions>(x => x.AllowSynchronousIO = true)
        .Configure<IISServerOptions>(x => x.AllowSynchronousIO = true);
// 使用MemoryCache 必须配置以下服务
builder.Services.AddDistributedMemoryCache();
// 使用Session 一般用于判断用户登录状态 HttpContext.Session
builder.Services.AddSession();
// Polly是一种.NET弹性和瞬态故障处理库，允许我们以非常顺畅和线程安全的方式来执诸如行重试，断路，超时，故障恢复等策略
builder.Services.AddHttpPollySetup();
// 控制器全局配置
builder.Services.AddControllers(o =>
{
    // 全局异常错误日志
    o.Filters.Add(typeof(GlobalExceptionsFilter));
    //o.Conventions.Insert(0, new GlobalRouteAuthorizeConvention());
    // 全局路由前缀公约
    o.Conventions.Insert(0, new GlobalRoutePrefixFilter(new RouteAttribute(RoutePrefix.Name)));
})
.AddNewtonsoftJson(options =>
{
    // Json 配置
    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
    options.SerializerSettings.ContractResolver = new DefaultContractResolver();
    options.SerializerSettings.DateFormatString = "yyyy-MM-dd HH:mm:ss";
    //options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
    options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Local;
    options.SerializerSettings.Converters.Add(new StringEnumConverter());
})
/*.AddFluentValidation(config =>
{
    //程序集方式添加验证
    config.RegisterValidatorsFromAssemblyContaining(typeof(UserRegisterVoValidator));
    //是否与MvcValidation共存
    config.DisableDataAnnotationsValidation = true;
})*/
;

// 注冊API发现功能(Minimal)
builder.Services.AddEndpointsApiExplorer();

builder.Services.Replace(ServiceDescriptor.Transient<IControllerActivator, ServiceBasedControllerActivator>());
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);


// 3、配置使用中间件
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    //app.UseHsts();
}

app.UseIpLimitMiddle();
app.UseRequestResponseLogMiddle();
app.UseRecordAccessLogsMiddle();
app.UseSignalRSendMiddle();
app.UseIpLogMiddle();
app.UseAllServicesMiddle(builder.Services);

app.UseSession();
app.UseSwaggerAuthorized();
app.UseSwaggerMiddle(() => Assembly.GetExecutingAssembly().GetManifestResourceStream("Blog.Core.Api.index.html"));

app.UseCors(Appsettings.app(new string[] { "Startup", "Cors", "PolicyName" }));
DefaultFilesOptions defaultFilesOptions = new DefaultFilesOptions();
defaultFilesOptions.DefaultFileNames.Clear();
defaultFilesOptions.DefaultFileNames.Add("index.html");
app.UseDefaultFiles(defaultFilesOptions);
app.UseStaticFiles();
app.UseCookiePolicy();
app.UseStatusCodePages();
app.UseRouting();

if (builder.Configuration.GetValue<bool>("AppSettings:UseLoadTest"))
{
    app.UseMiddleware<ByPassAuthMiddleware>();
}
app.UseAuthentication();
app.UseAuthorization();
app.UseMiniProfilerMiddleware();
//app.UseExceptionHandlerMidd();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    endpoints.MapHub<ChatHub>("/api2/chatHub");
});


var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
var myContext = scope.ServiceProvider.GetRequiredService<MyContext>();
var tasksQzServices = scope.ServiceProvider.GetRequiredService<ITasksQzServices>();
var schedulerCenter = scope.ServiceProvider.GetRequiredService<ISchedulerCenter>();
var lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
app.UseSeedDataMiddle(myContext, builder.Environment.WebRootPath);
app.UseQuartzJobMiddleware(tasksQzServices, schedulerCenter);
app.UseConsulMiddle(builder.Configuration, lifetime);
app.ConfigureEventBus();

// 4、运行
app.Run();
