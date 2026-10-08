# Implantação central no Portainer, Docker e Traefik

## Organização

Uma stack cfc-central atende todas as escolas no mesmo domínio. O volume cfc-central-data contém o cadastro da plataforma e uma pasta tenants/<school-id> para cada escola, com banco e recursos próprios. Filiais são Units dentro desse banco, com IDs estáveis. Não é necessário criar domínio, contêiner ou deploy para cada cliente.

Esta entrega usa Docker Standalone/Compose. Execute uma réplica: o registro central, SQLite e navegador persistente têm um dono por volume. Não monte esse volume em múltiplos servidores escritores nem trate a stack como configuração pronta para Swarm.

## Imagens e proxy

Após Verify aprovado, publique a versão por tag v0.4.0 ou execute Publish versioned images. As imagens são ghcr.io/zionlab7/cfc-plus:0.4.0 e :0.4.0-browser. A existência dos exemplos não significa que as imagens já foram publicadas. Pode construir na VPS:

~~~sh
docker build --target runtime -t cfc-plus:0.4.0 .
docker build --target browser -t cfc-plus:0.4.0-browser .
~~~

Prefira o digest aprovado em produção. A visibilidade GHCR é independente da do repositório. O contexto Docker usa lista explícita de código e exclui dados de clientes.

Reutilize Traefik, rede, entrypoint websecure e certresolver existentes. Informe o IP real do proxy em TRAEFIK_IP. O exemplo traefik.yaml cria cfc-proxy em 172.30.0.0/24 e proxy em 172.30.0.2; ajuste conflitos de rede, ACME_EMAIL e DNS. Publique só 80/443. Não exponha browser, banco ou dashboard administrativo à internet.

## Criar a stack

1. Copie chromium-seccomp.json para /etc/cfcplus/chromium-seccomp.json no host Docker para usar Chromium com sandbox.
2. Portainer → Stacks → Git repository → zionLab7/cfc-plus → main → infra/production/compose.browser.yaml. Para gestão sem GOV, use compose.yaml.
3. Configure as variáveis de .env.example, incluindo domínio CENTRAL, imagem aprovada, proxy, volume e recursos.
4. Confira o healthcheck e abra o endereço HTTPS central. O app não aceita cabeçalhos de proxy de qualquer origem nem login comercial por HTTP.
5. Abra /platform/ e use o acesso inicial do arquivo privado /data/platform-first-access.json. O dono deve trocar essa senha antes de cadastrar escolas. Consulte o arquivo via console privada do contêiner no Portainer; não publique seu conteúdo.
6. Cadastre ID e nome de cada escola no painel. A base é criada sem dados fictícios em /data/tenants/<id>. O acesso inicial admin fica em first-access.json dessa pasta. Entregue-o por canal privado ao administrador da escola; ele deve trocar a senha no primeiro acesso.
7. O administrador cadastra suas filiais/equipe ou importa sua base em Instalação e importação. Todos acessam o mesmo endereço com o ID da escola e sua conta pessoal.

O cadastro só aceita ID de 3 a 48 caracteres: letra minúscula inicial, letras, dígitos e hífen. Login de ID desconhecido não cria escola. Alterar um header ou query não troca o banco de uma sessão assinada. O app vincula também cache, contexto do aluno e fila offline a escola + usuário.

## WhatsApp e navegador

Configure Evolution em Integrações DE CADA ESCOLA, usando instância exclusiva. As configurações privadas ficam no diretório dela. O modo central não herda uma chave/instância global para todos os clientes. Evolution e seu PostgreSQL são serviço privado separado, com backups próprios.

CFC_BROWSER_PROFILES limita perfis por escola; CFC_CENTRAL_BROWSER_PROFILES limita o total no servidor (padrão 12). O limite recusa novas aberturas com mensagem clara; não encerra uma sessão ocupada automaticamente. Aumente conforme medição de RAM/CPU. Cookies/perfis persistentes são próprios de cada escola/profissional; GOV/DETRAN podem expirar a autenticação e exigir nova verificação humana.

## Atualizações e Windows

Atualize UMA imagem central após backup e homologação. Não habilite deploy automático de cada commit no ambiente com alunos reais. O workflow Verify produz o instalador Windows compartilhado. Publique o .exe aprovado em /data/downloads/CFC-Plus-Windows.exe; o mesmo instalador serve a todas as escolas e conecta ao domínio central.

Para adotar uma base operacional já existente no piloto, o operador do servidor pode usar --adopt-school <id> --school-name <nome> --legacy-data <pasta-existente> com modo central. Esse comando preserva a pasta/chaves existentes e não é exposto pela API. Backups dessa exceção precisam incluir a pasta externa referenciada. Na VPS nova, prefira importação pelo app em tenants/<id>; DPAPI Windows não pode ser simplesmente copiado para Linux.

Referências: [Portainer](https://docs.portainer.io/user/docker/stacks/add), [Traefik Docker](https://doc.traefik.io/traefik/reference/install-configuration/providers/docker/), [proxy ASP.NET](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).
