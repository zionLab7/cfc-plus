# Implantação no Portainer, Docker e Traefik

## Organização dos clientes

Uma stack por autoescola: `cfc-escola-exemplo`. Cada uma tem `CFC_SCHOOL_ID` exclusivo, domínio próprio, volume próprio, banco SQLite, arquivos protegidos e sessões independentes. Filiais ficam na mesma stack. Não monte o mesmo volume em duas escolas nem execute duas réplicas do app sobre o mesmo SQLite. O servidor verifica a identidade do volume antes de iniciar.

Esta entrega usa Docker Standalone/Compose no Portainer. Não trate estas stacks como configuração pronta para Docker Swarm.

## Imagens

O repositório público contém os workflows `Verify` e `Publish versioned images`. Após a verificação, crie a tag da versão ou execute o workflow de publicação. Ele produz:

- `ghcr.io/zionlab7/cfc-plus:0.3.0`: gestão e portais.
- `ghcr.io/zionlab7/cfc-plus:0.3.0-browser`: também inclui Electron/Chromium interativo e Xvfb.

São instruções de publicação, não indicação de que as imagens já estão no GHCR. A visibilidade do pacote GHCR é independente da do repositório. Se o pacote ficar privado, configure no Portainer uma credencial somente de leitura; se optar por pacote público, o pull dispensa essa credencial. Não coloque tokens no Compose, Git ou imagem. Prefira fixar o digest da imagem aprovada no ambiente real.

Para construir na VPS sem GHCR, clone o repositório público e use:

```sh
docker build --target runtime -t cfc-plus:0.3.0 .
docker build --target browser -t cfc-plus:0.3.0-browser .
```

O build usa uma lista explícita de arquivos de código. Não copia `App_Data`, SQL/CSV de clientes nem o pacote de origem.

## Traefik

Se já há Traefik, reutilize sua rede, IP interno, entrypoint `websecure` e resolvedor de certificado. Informe o IP real em `TRAEFIK_IP`; o app só aceita cabeçalhos de proxy desse endereço e loopback. Não use configuração que confia em qualquer proxy.

Para uma VPS nova, `infra/production/traefik.yaml` cria a rede `cfc-proxy` em `172.30.0.0/24` e Traefik em `172.30.0.2`. Ajuste caso esse bloco conflite com redes existentes. Configure `ACME_EMAIL`, aponte DNS para a VPS e publique somente 80/443. O dashboard não é exposto. O socket Docker só é montado no proxy, não no app; operadores do Traefik têm acesso sensível ao host.

## Stack completa com sessões

1. No host Docker, copie `infra/production/chromium-seccomp.json` para `/etc/cfcplus/chromium-seccomp.json`, legível pelo Docker. O perfil da Microsoft acrescenta as permissões de namespaces necessárias ao sandbox; não é `seccomp=unconfined`.
2. No Portainer → Stacks → Add stack → Git repository, escolha o repositório, `main` e Compose path **`infra/production/compose.browser.yaml`**. Também pode colar o arquivo no Web editor.
3. Carregue as variáveis do exemplo `infra/production/.env.example`, substituindo domínio, escola e imagem. Nunca deixe `escola-teste` ou domínios de exemplo em produção. Configure `CHROMIUM_SECCOMP_PATH` com o caminho absoluto no host Docker.
4. Ajuste `CFC_MEMORY_LIMIT`, `CFC_CPUS` e `CFC_BROWSER_PROFILES` à capacidade medida. O padrão de perfis é seis. Não publique portas do browser nem da base. A stack usa 1 GB de memória compartilhada interna para o Chromium.
5. Deploy. Confirme status saudável e abra o domínio HTTPS. O app recusa APIs comerciais por HTTP. Se houver erro de origem/HTTPS, confira `TRAEFIK_IP` em vez de remover a verificação.

Para operar sem sessões governamentais, escolha **`infra/production/compose.yaml`** e `CFC_IMAGE`. Não precisa do perfil seccomp nesse modo.

## Primeiro administrador

No console do contêiner no Portainer, execute `cat /data/first-access.json` e leia o acesso inicial em ambiente privado. A senha nunca vai aos logs nem ao repositório. Entre pelo HTTPS e troque-a na tela obrigatória. O arquivo inicial é removido após a troca. Alternativamente, provisione um secret do Docker com senha de 12 a 128 caracteres e configure `CFC_ADMIN_PASSWORD_FILE`; ele é usado apenas na inicialização de um volume vazio.

Cadastre unidades, pacotes, instrutores, veículos e regras, ou importe os dados antes de começar o atendimento. Crie os usuários da equipe e os acessos vinculados de aluno/instrutor. Senhas iniciais precisam ser substituídas por cada titular.

## Sessões no servidor

Os perfis ficam em `/data`, com checkpoints protegidos. Desligar o computador de atendimento não encerra o servidor nem apaga os perfis. Reiniciar o contêiner conserva os dados do navegador e tenta retomar acessos habilitados. O prazo da sessão continua sendo determinado pelo GOV.BR/DETRAN: expiração, CAPTCHA, MFA e autenticação pessoal podem exigir novo login. A escola confirma a identidade da conta antes de utilizar a sessão.

Na primeira implantação Linux, teste um profissional e um aluno reais com o operador, incluindo persistência após fechar o cliente e após reiniciar a stack. Os testes locais sintéticos aprovados não homologam a autenticação de uma VPS específica.

## Instalador

O workflow Verify gera um artefato Windows; o build local também gera o EXE. Para distribuí-lo aos clientes, copie o **instalador** aprovado para `/data/downloads/CFC-Plus-Windows.exe`. Pode usar `docker cp` no host, mantendo leitura pelo UID 1654. A página `/install/` mostra automaticamente o botão quando o arquivo está presente. Não copie o EXE do servidor como se fosse instalador.

Atualize a imagem por versão/digest após backup e teste. Não habilite atualização automática a cada commit em uma stack com alunos reais. Mantenha Portainer sob acesso administrativo privado e autenticação própria.

Referências: [stacks e variáveis no Portainer](https://docs.portainer.io/user/docker/stacks/add), [roteamento Docker do Traefik](https://doc.traefik.io/traefik/reference/install-configuration/providers/docker/), [proxy confiável no ASP.NET](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0), [sandbox e namespaces de Chromium em Docker](https://playwright.dev/docs/docker).

## WhatsApp com Evolution

A conexão existente aceita `EVOLUTION_BASE_URL`, `EVOLUTION_INSTANCE` e `EVOLUTION_API_KEY` da stack. Crie uma instância exclusiva para cada autoescola no seu serviço Evolution e preencha a chave somente nas variáveis privadas do Portainer. Não inclua a chave em arquivos versionados. Abra Integrações, conecte pelo QR Code e confira envio e histórico. Sem essas variáveis, o app mostra “Não configurada”. O Compose em `infra/evolution/` serve ao desenvolvimento local; sua porta de loopback não é alcançável por outro contêiner. Para produção, use um serviço privado acessível pela rede Docker ou HTTPS, com volumes e backup próprios. O servidor Evolution e seu PostgreSQL não estão embutidos no banco SQLite de cada escola.
