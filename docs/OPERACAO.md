# Operação, backup e recuperação

O volume contém SQLite, anexos, chaves de proteção, configurações privadas, histórico de importação e perfis/cookies do navegador. Dados e chaves precisam ser recuperados juntos. Não monte `/data` em diretório público nem em armazenamento de sincronização de arquivos. Não copie SQLite em uso por simples cópia de arquivo.

## Backup completo

`infra/production/backup.sh <school-id> <diretório-absoluto> [volume]` para a stack daquela escola, cria um tar privado de todo o volume e reinicia exatamente os contêineres parados. Execute em janela combinada; há indisponibilidade durante a cópia. O terceiro argumento atende instalações que usam `CFC_DATA_VOLUME` personalizado.

O arquivo contém dados pessoais e chaves. Criptografe-o e mantenha cópia fora da VPS, com retenção definida e acesso restrito. O script não envia nem criptografa automaticamente o backup. Agende backup diário e teste recuperação antes de aceitar clientes reais.

`infra/production/restore.sh <school-id-original> <backup-absoluto> [novo-volume]` recusa volume existente e restaura em volume novo. Configure `CFC_DATA_VOLUME` com o novo nome e mantenha o `CFC_SCHOOL_ID` original. Verifique o conteúdo antes de trocar a stack para o volume recuperado. O volume anterior continua preservado. Só restaure backups criados e confiáveis; o script não substitui verificação de proveniência.

## Monitoramento

A stack tem healthcheck a cada 30 segundos e logs com limite de tamanho. Monitore também disco livre, memória, CPU, respostas 5xx e tempo de resposta. `unhealthy` é um alerta; o Docker Standalone não reinicia automaticamente um processo só porque o healthcheck falhou. A política `restart: unless-stopped` reinicia quando o processo encerra.

Uma importação é isolada do atendimento, mas compartilha os recursos da stack. Execute cargas grandes fora do pico e reserve disco/RAM. Reinício durante importação marca o job como Interrompida, preservando a base anterior. Não configure múltiplos workers nem múltiplas réplicas escrevendo o mesmo volume.

## Proteção e distribuição

Modo comercial exige HTTPS, cookies Secure/HttpOnly/SameSite, origem da operação e proxy explicitamente confiável. Senhas usam PBKDF2 e a troca revoga sessões antigas. Credenciais, anexos e checkpoints governamentais têm proteção existente baseada em chaves persistentes. Linux não usa DPAPI Windows: importar novamente o export gera proteção com as chaves da VPS. Uma cópia de arquivos protegidos via DPAPI não é migração suficiente.

Não publicar exports, `App_Data`, logs, arquivos de acesso inicial, tokens, certificados privados ou evidências com dados de alunos. A lista de publicação e o contexto Docker excluem esses materiais. Não conceder aos clientes acesso ao Portainer ou ao socket Docker para que usem o app.

O instalador ainda não tem assinatura de código comercial. Sistemas Windows podem mostrar aviso de editor não reconhecido; adquirir e configurar assinatura deve fazer parte da distribuição comercial final. Não instrua clientes a desligar proteção do Windows.
