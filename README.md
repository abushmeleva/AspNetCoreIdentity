# AspNetCoreIdentity
Реализация ASP.NET Core Identity и JWT на ASP.NET Core 3.1

## Начальная конфигурация
Добавим конфигурацию для использования DataContext EntityFramework

```csharp
services.AddDbContext<DataContext>(opt =>
	opt.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));
```
И строку подключения к MSSQL базе в appsettings.json


```json
"ConnectionStrings": {"DefaultConnection": "Server=.\\SQL_2016;Database=AspNetCoreIdentity;user id=testUser;password='testUser'"}
```

Добавим MediatR, чтобы реализовать в нашем приложение подход CQRS (о котором можно почитать [здесь](https://medium.com/@dbottiau/a-naive-introduction-to-cqrs-in-c-9d0d99cd2d54)) 

```csharp
services.AddMediatR(typeof(LoginHandler).Assembly);
```

## Добавляем ASP.NET Core Identity в проект
Первым делом добавим в Domain пользовательский класс AppUser.cs с необходимыми под наши задачи полями и унаследуем его от IdentityUser.

```csharp
public class AppUser : IdentityUser                                                                                 
{
  public string DisplayName { get; set; }
}
```

Теперь зарегистрируем, созданный нами класс пользователя в Startup.cs 

```csharp
var builder = services.AddIdentityCore<AppUser>();
var identityBuilder = new IdentityBuilder(builder.UserType, builder.Services);
```

Добавим в проект EFData DataContext, унаследованный от IdentityDbContext 

```csharp
public class DataContext : IdentityDbContext<AppUser>
{
  public DataContext(DbContextOptions<DataContext> options) : base(options) { }
}
```

Установим тип хранилища (класс контекста данных) в Startup.cs, которое Identity будет использовать для хранения данных.


```csharp
identityBuilder.AddEntityFrameworkStores<DataContext>();
identityBuilder.AddSignInManager<SignInManager<AppUser>>();
```

Подключим аутентификацию в методе Configure() класса Startup.cs

```csharp
app.UseAuthentication();
```

На этом этапе мы уже можем создать инициальную миграцию, для этого стартовым проектом выбираем API, открываем Package Manager Console и в Default project выбираем EFData, запускаем создание инициальной миграции командой 

```csharp
> Add-migration initial
```
![alt 1](https://i.postimg.cc/VkfDM80f/5.png)

Результатом будет автоматически созданная миграция в папке Migrations проекта EFData, прежде чем ее накатить на нашу базу, создадим DataSeed для автозаполнения начальными данными базы, в том же проекте, создаем класс DataSeed. 

```csharp
public class DataSeed
{
  public static async Task SeedDataAsync(DataContext context, UserManager<AppUser> userManager)
  {
    if (!userManager.Users.Any())
    {
      var users = new List<AppUser>
                      {
                        new AppUser
                            {
                              DisplayName = "TestUserFirst",
                              UserName = "TestUserFirst",
                              Email = "testuserfirst@test.com"
                            },
                        new AppUser
                            {
                              DisplayName = "TestUserSecond",
                              UserName = "TestUserSecond",
                              Email = "testusersecond@test.com"
                             }
                         };

                foreach (var user in users)
                {
                    await userManager.CreateAsync(user, "qazwsX123@");
                }
    }
  }
}
```

Теперь можно запускать создание и обновление базы

```csharp
> update-database
```

Результат 
![alt 1](https://i.postimg.cc/C1CzgWht/6.png)

Все необходимое для работы с Identity добавленно, теперь реализуем метод для авторизации пользователя в нашем приложение. 

В UserController проекта API добавим метод LoginAsync.


```csharp
[HttpPost("login")]
public async Task<ActionResult<User>> LoginAsync(LoginQuery query)
{
  return await Mediator.Send(query);
}
```

В проект Application добавим три класса:
LoginHandler.cs 
```csharp
public class LoginHandler : IRequestHandler<LoginQuery, User>
{
  private readonly UserManager<AppUser> _userManager;

  private readonly SignInManager<AppUser> _signInManager;

  public LoginHandler(UserManager<AppUser> userManager,SignInManager<AppUser> signInManager)
  {
    _userManager = userManager;
    _signInManager = signInManager;
  }

  public async Task<User> Handle(LoginQuery request, CancellationToken cancellationToken)
  {
    var user = await _userManager.FindByEmailAsync(request.Email);
    if (user == null)
    {
      throw new RestException(HttpStatusCode.Unauthorized);
    }

    var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);

    if (result.Succeeded)
    {
      return new User
                 {
                    DisplayName = user.DisplayName,
                    Token = "test", (Далее здесь будет вызов метода сервиса, генерирующий Token)                         
                    UserName = user.UserName,
                    Image = null
                  };
      }

      throw new RestException(HttpStatusCode.Unauthorized);
    }
}
```

LoginQuery.cs 

```csharp
public class LoginQuery : IRequest<User>
{
  public string Email { get; set; }
  public string Password { get; set; }
}
```


LoginQueryValidation.cs

```csharp
public class LoginQueryValidation : AbstractValidator<LoginQuery>
{
  public LoginQueryValidation()
  {
    RuleFor(x => x.Email).NotEmpty();
    RuleFor(x => x.Password).NotEmpty();
  }
}
```

Протестируем метод при помощи любого API клиента (в нашем случае Postman), сделав запрос к веб-приложению (примеры моих запросов для Postman так же добавены на GitHub)
Регистрация с результатом 401 Unauthorized

![alt 7](https://i.postimg.cc/Bn8tmmzr/7.png)

Успешная регистрация с данными по пользователю в респонсе

![alt 8](https://i.postimg.cc/q78S0dY2/8.png)

Добавим набор ограничений в Startup.cs, теперь каждый запрос к нашему API должен быть авторизован, единственный метод, который будет являться исключением (в дальнейшем их может быть больше) - это Login, для этого на UserController навесим атрибут [AllowAnonymous]


```csharp

services.AddMvc(option => 
{
        option.EnableEndpointRouting = false;
        var policy = new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser().RequireAuthenticatedUser().Build();
		option.Filters.Add(new AuthorizeFilter(policy));
	}).SetCompatibilityVersion(CompatibilityVersion.Latest);

```

## Добавляем JWT в проект
Конфигурация взаимодейсвия нашего приложения с JWT производится достаточно просто.
В проекте Application определим отдельный интерфейс IJwtGenerator с единственным методом CreateToken


```csharp

public interface IJwtGenerator
{
  string CreateToken(AppUser user);
}

```
В проект Infrastructure добавим класс JwtGenerator и унаследуем его от IJwtGenerator, со следующей реализацией  CreateToken

```csharp

public class JwtGenerator : IJwtGenerator
{
  private readonly SymmetricSecurityKey _key;

  public JwtGenerator(IConfiguration config)
  {
    _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["TokenKey"]));
  }

  public string CreateToken(AppUser user)
  {
    var claims = new List<Claim> { new Claim(JwtRegisteredClaimNames.NameId, user.UserName) };
            
    var credentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha512Signature);

    var tokenDescriptor = new SecurityTokenDescriptor
    {
      Subject = new ClaimsIdentity(claims),
      Expires = DateTime.Now.AddDays(7),
      SigningCredentials = credentials
    };
    
    var tokenHandler = new JwtSecurityTokenHandler();
    var token = tokenHandler.CreateToken(tokenDescriptor);
    return tokenHandler.WriteToken(token);
  }
}

```

Зарегистрариуем реализацию в контейнере приложения

```csharp
services.AddScoped<IJwtGenerator, JwtGenerator>();
```

Настроим проверку JWT

```csharp
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("super secret key"));
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(
                opt =>
                     {
                        opt.TokenValidationParameters = new TokenValidationParameters
                                                            {
                                                              ValidateIssuerSigningKey = true,
                                                              IssuerSigningKey = key,
                                                              ValidateAudience = false,
                                                              ValidateIssuer = false,
                                                            };
                      });	

```

#### Защита данных
Сейчас значение строки указано в открытом виде и берется даже не из конфигов, для того, чтобы безопасно хранить секретные данные, предлагаю воспользоваться .NET user secret, подробнее об этом инструменте можно почитать здесь, я лишь добавлю что итоговый файл хранит незашифрованные данные, поэтому мы будем использовать его только во время разработке.

Для того чтобы включить секретное хранилище воспользуемся командой в PackageManager Console

```csharp
> dotnet user-secrets init 
```

В API.csproj добавим UserSecretsId
![alt 9](https://i.postimg.cc/j2JHfyfN/9.png)

Теперь в наше секретное хранилище можем поместить значение следующей командой

```csharp
> dotnet user-secrets set "TokenKey" "super secret key" –p API/
```

![alt 10](https://i.postimg.cc/sg2NBNXZ/10.png)

Для того чтобы просмотреть все что сейчас находится в хранилище, нужно воспользоваться командой

```csharp
> dotnet user-secrets list –p API/
```

![alt 11](https://i.postimg.cc/zf83SKsW/11.png)

После этого мы можешь использовать наши секретные данные следующим образом

```csharp
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["TokenKey"]));
```

Теперь мы можем испольовать наш  JwtGenerator в LoginHandler

```csharp
return new User
{
  DisplayName = user.DisplayName,
  Token = _jwtGenerator.CreateToken(user),
  UserName = user.UserName,
  Image = null
};
```

Наш ответ при регистрации изменится и будет возвращать токен, с которым мы можем идти на сервер за данными

![alt 12](https://i.postimg.cc/SRxML5vw/12.png)

Проверим работу веб-приложения, для того, чтобы запрос выполнился успешно в параметрах headers необходимо передать наш токен

![alt 13](https://i.postimg.cc/Vv2kSVQ9/13.png)

В случае, если токена не будет, вернется ошибка 401 Unauthorized

![alt 14](https://i.postimg.cc/pT6276TN/14.png)


#### Полезные ссылки
[The Onion Architecture] (https://jeffreypalermo.com/2008/07/the-onion-architecture-part-1/)
[CQRS] (https://medium.com/@dbottiau/a-naive-introduction-to-cqrs-in-c-9d0d99cd2d54)
