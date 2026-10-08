# Operação do servidor central

O volume contém registro de escolas, conta do dono, chaves do servidor e tenants/<id> com SQLite, anexos protegidos, importações, configurações privadas e perfis/cookies. Preserve dados e chaves juntos. Use disco local/NVMe, sem OneDrive ou sincronização de arquivos. Nunca faça cópia simples de SQLite enquanto houver escrita.

## Backup e recuperação

infra/production/backup.sh central <diretório-absoluto> [volume] para exatamente os contêineres usando o volume central, cria tar privado de TODO o volume e reinicia os contêineres parados, inclusive se a cópia falhar. Padrão cfc-central-data. Há indisponibilidade de todas as escolas durante esse backup consistente: execute em janela planejada.

infra/production/restore.sh central <backup-absoluto> [novo-volume] recusa volume existente e restaura em volume NOVO. Configure CFC_DATA_VOLUME na única stack com esse novo volume após conferência. O registro e IDs de todas as escolas são preservados; mantenha o volume anterior para recuperação. Só restaure arquivos criados e confiáveis.

O backup contém dados pessoais e chaves. Criptografe e armazene fora da VPS, com retenção e acesso restrito. Os scripts não fazem criptografia/envio automaticamente. Teste recuperação antes de clientes reais. Se usou --adopt-school com pasta externa ao volume, inclua também essa pasta num backup consistente e preserve a referência; os scripts do volume não alcançam diretórios externos.

## Monitoramento

Monitore healthcheck, erros 5xx, latência de busca/agenda/financeiro, CPU, RAM, disco e volume de filas. Healthcheck saudável indica processo disponível, não certifica cada integração externa. Docker Standalone não reinicia só por unhealthy; restart: unless-stopped cobre encerramento do processo.

Importações têm limite global de duas conferências e staging por escola. Limite global de navegadores CFC_CENTRAL_BROWSER_PROFILES e limite por escola CFC_BROWSER_PROFILES controlam aberturas. Expansão de ZIP, Electron e anexos consomem disco/RAM. Não configure múltiplos processos escritores no mesmo volume. Sessão GOV expirada exige reautenticação humana; persistência de perfil não garante login eterno.

## Proteção

Produção exige HTTPS, cookie Secure/HttpOnly/SameSite e proxy explicitamente confiável. A escola é uma claim assinada da sessão; IDs/headers incompatíveis são recusados antes de resolver dados. Cada escola tem suas chaves para anexos e credenciais. Troca de senha revoga cookies antigos. No primeiro acesso, tanto dono quanto administrador trocam a senha inicial.

Linux não usa DPAPI Windows. Reimporte os dados e revalide recursos protegidos para migração. Não publique SQL/CSV, App_Data*, logs, credenciais, certificados ou evidências com dados pessoais. Não dê acesso Portainer/socket Docker aos usuários de escolas. O acesso ao dono /platform/ e aos arquivos iniciais é operacional e privado.
