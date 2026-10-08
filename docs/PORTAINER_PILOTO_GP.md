# Instalar o CFC+ na VPS da GP pelo Portainer — piloto de um mês

Este roteiro mantém o app central e o navegador de servidor em UMA stack, junto com Evolution, PostgreSQL da Evolution e Redis. Reutiliza o Docker, Portainer e Traefik existentes. Não cria outro Traefik, não publica portas extras e não altera a outra aplicação da VPS.

Ambiente informado: Hostinger KVM 2, Linux x86_64, 2 vCPUs, 8 GB RAM, 100 GB disco; rede Docker `cfcplus`. Domínio, certresolver e IP do Traefik ainda precisam ser preenchidos. O plano vence em 11/10/2026: confira a renovação para cobrir o mês inteiro.

## 1. O que fica no servidor e o que fica no Windows

- VPS: banco de cada escola, filiais, documentos, agenda, financeiro, portais, WhatsApp e perfis de navegador do servidor.
- Windows com instalador 0.4.1+: opção **Abrir neste computador**, para o portal usar o ambiente local e os certificados disponibilizados pelo middleware do cartão.
- As sessões locais e do servidor são independentes. A tela do navegador na VPS NÃO redireciona cartão/USB do computador do atendente.
- Fechar uma janela local oculta o portal; o ícone CFC+ fica na bandeja enquanto houver portais locais. **Aplicativo → Encerrar portais locais** encerra os processos preservando o perfil. **Sair do CFC+** encerra o cliente.
- Ao desligar o Windows, os processos param. Na próxima abertura do perfil, tentamos retomar os cookies ainda válidos e protegidos no Windows. O portal pode pedir novo login, CAPTCHA ou PIN. Não prometemos autenticação eterna.
- O perfil local pertence ao servidor configurado, escola, profissional e usuário do Windows. Outro computador tem outro perfil. Compartilhamento remoto de um navegador local entre máquinas ainda não foi implementado.
- O diagnóstico local reconhece leitores PC/SC e procura componentes Nitgen. Seleção de certificado não equivale à homologação do cartão real. **Captura Hamster III e transmissão/validação de biometria no DETRAN ainda dependem de driver/SDK, protocolo e teste com o equipamento real.** Não use o navegador da VPS como solução para essa pendência.

## 2. Conferir o ambiente antes de instalar

No Portainer, selecione um ambiente **Docker Standalone**. Este Compose não é uma stack Swarm; security_opt, limites e depends_on precisam ser respeitados. Se seu ambiente aparece como Swarm, não use este arquivo sem adaptar e validar.

No terminal da VPS:

~~~sh
docker version
docker info --format '{{.Architecture}}'
free -h
df -h
docker stats --no-stream
docker network inspect cfcplus
~~~

Se a rede não existir, crie pelo Portainer → Networks → Add network, nome `cfcplus`, driver bridge, ou execute `docker network create cfcplus`. Na configuração da stack EXISTENTE do Traefik, acrescente essa rede externa ao serviço do Traefik e redeclaração em networks. Preserve sua configuração atual. Não remova as redes que atendem sua outra aplicação.

Identifique o nome real do container Traefik no Portainer e seu IPv4 na rede cfcplus. Também pode consultar, substituindo o nome:

~~~sh
docker inspect --format '{{with index .NetworkSettings.Networks "cfcplus"}}{{.IPAddress}}{{end}}' NOME_REAL_DO_CONTAINER_TRAEFIK
~~~

Esse endereço é TRAEFIK_IP. Prefira IP fixo na configuração existente do Traefik, dentro do IPAM da rede, sem colidir com outros serviços. Se o IP mudar, atualize a variável do CFC+; o aplicativo só confia no proxy informado.

Confira na configuração existente do Traefik os nomes de:
- entrypoint HTTPS (exemplo: websecure);
- certresolver ACME (use seu nome real, não um nome inventado);
- provider Docker e disponibilidade da rede cfcplus.

Crie o registro DNS A do domínio escolhido para o IPv4 da VPS. Use somente hostname em CFC_DOMAIN, sem https://, barras ou caminhos. Durante a primeira validação, DNS direto simplifica o diagnóstico. Um AAAA só deve existir se o IPv6 estiver configurado corretamente.

## 3. Publicar a versão antes do deploy

O Compose usa uma imagem pronta; importar o repositório NÃO compila o app na VPS. Isso evita uma compilação pesada junto da aplicação que já roda no KVM 2.

1. Abra [Actions](https://github.com/zionLab7/cfc-plus/actions) e espere **Verify** do commit escolhido ficar verde.
2. Abra **Publish versioned images** → **Run workflow**, escolha main e execute.
3. Espere os jobs publish e windows-release concluírem. Este fluxo publica as imagens de versão e o instalador Windows com SHA-256.
4. No GitHub → Packages, confira se o pacote `cfc-plus` é público. Repositório público e pacote GHCR público são configurações distintas. Para manter pacote privado, cadastre o registry ghcr.io e uma credencial de leitura no Portainer.
5. A imagem desta entrega é `ghcr.io/zionlab7/cfc-plus:0.4.1-browser`. Confirme seu pull na VPS:

~~~sh
docker pull ghcr.io/zionlab7/cfc-plus:0.4.1-browser
docker image inspect ghcr.io/zionlab7/cfc-plus:0.4.1-browser --format '{{index .RepoDigests 0}}'
~~~

Pode usar o digest retornado em CFC_BROWSER_IMAGE para fixar exatamente a imagem aprovada. O instalador aparece em [Releases](https://github.com/zionLab7/cfc-plus/releases) quando windows-release terminar. Se o fluxo ainda não foi executado, não há garantia de que a imagem ou release exista.

## 4. Variáveis: copiar e preencher no Portainer

Use `infra/production/pilot.env.example` como modelo. Guarde a versão preenchida somente no Portainer ou em arquivo privado, nunca no GitHub.

~~~dotenv
CFC_BROWSER_IMAGE=ghcr.io/zionlab7/cfc-plus:0.4.1-browser
CFC_DOMAIN=PREENCHER_DOMINIO_SEM_HTTPS
TRAEFIK_NETWORK=cfcplus
TRAEFIK_IP=PREENCHER_IP_DO_TRAEFIK_NA_REDE_CFCPLUS
TRAEFIK_ENTRYPOINT=websecure
TRAEFIK_CERTRESOLVER=PREENCHER_NOME_REAL_DO_RESOLVER
EVOLUTION_API_KEY=PREENCHER_CHAVE_HEXADECIMAL_64_CARACTERES
EVOLUTION_POSTGRES_PASSWORD=PREENCHER_OUTRA_CHAVE_HEXADECIMAL_64_CARACTERES
CFC_SUPPORT_EMAIL=
CFC_ADMIN_LOGIN=admin
CFC_DATA_VOLUME=cfc-gp-central-data
EVOLUTION_INSTANCES_VOLUME=cfc-gp-evolution-instances
EVOLUTION_POSTGRES_VOLUME=cfc-gp-evolution-postgres
EVOLUTION_REDIS_VOLUME=cfc-gp-evolution-redis
CFC_MEMORY_LIMIT=3g
CFC_CPUS=1.0
CFC_BROWSER_PROFILES=1
CFC_CENTRAL_BROWSER_PROFILES=1
EVOLUTION_MEMORY_LIMIT=768m
EVOLUTION_CPUS=0.5
POSTGRES_MEMORY_LIMIT=256m
POSTGRES_CPUS=0.2
REDIS_MEMORY_LIMIT=256m
REDIS_CPUS=0.1
~~~

| Variável | O que informar |
|---|---|
| CFC_BROWSER_IMAGE | Imagem publicada e aprovada, ou seu digest. |
| CFC_DOMAIN | Hostname que você configurou no DNS. |
| TRAEFIK_NETWORK | cfcplus; a rede deve existir e conter o Traefik. |
| TRAEFIK_IP | IP real do Traefik NESSA rede. |
| TRAEFIK_ENTRYPOINT | Nome do entrypoint HTTPS existente. Ajuste websecure se diferente. |
| TRAEFIK_CERTRESOLVER | Nome exato do resolver já configurado no Traefik. |
| EVOLUTION_API_KEY | Chave privada para a API Evolution, somente hexadecimal. |
| EVOLUTION_POSTGRES_PASSWORD | Outra chave privada hexadecimal, diferente da API. |
| CFC_SUPPORT_EMAIL | Seu contato de suporte; pode ficar vazio no piloto. |
| CFC_ADMIN_LOGIN | Login inicial dos administradores das escolas novas. O dono da plataforma usa platform-admin. |
| As quatro variáveis VOLUME | Nomes dos volumes exclusivos desta instalação; mantenha nas atualizações. |
| MEMORY_LIMIT / CPUS | Tetos de consumo, sem garantia/reserva de capacidade. |
| CFC_BROWSER_PROFILES | Limite de perfis abertos por escola NO SERVIDOR. |
| CFC_CENTRAL_BROWSER_PROFILES | Limite de perfis abertos em todo o servidor. Não inclui os locais do Windows. |

Gere os dois segredos separadamente na VPS e guarde os valores num gerenciador de senhas:

~~~sh
openssl rand -hex 32
openssl rand -hex 32
~~~

A senha hexadecimal evita caracteres que precisariam ser escapados na URI do PostgreSQL. Nunca use os textos PREENCHER como senha. O Compose não tem senha de administrador: ela é gerada aleatoriamente e entregue em arquivo privado no primeiro acesso.

Os limites somam aproximadamente **4,25 GiB de RAM e 1,8 vCPU**. Os 1 GiB de /dev/shm do app estão dentro do orçamento de memória do container, não são uma reserva extra. O espaço restante é para Linux, Traefik, Portainer, a aplicação existente e picos. Primeiro meça o consumo do que já roda. Com esse piloto, abra no máximo um perfil de servidor por vez e execute uma importação por vez, em horário tranquilo. Se houver OOM/reinícios, não aumente limites sem conferir a margem da VPS.

## 5. Criar UMA stack pelo repositório

Portainer → Stacks → Add stack:
- Name: `cfc-gp-piloto`.
- Método: Git Repository.
- Repository URL: `https://github.com/zionLab7/cfc-plus.git`.
- Authentication: desativada, pois o repositório é público.
- Repository reference: `refs/heads/main`, ou a tag de release validada.
- Compose path: `infra/production/compose.pilot.yaml`.
- Additional paths: nenhum.
- Environment variables: adicione as variáveis acima; pode carregar um .env PRIVADO preenchido.
- GitOps/atualização automática: desativada durante o mês de teste.
- Deploy the stack.

O filtro seccomp Chromium aprovado está incorporado no Compose; não precisa copiar um arquivo para o host nem montar arquivos dentro do Portainer. Não substitua por privileged ou seccomp=unconfined.

A stack terá app, evolution, postgres e redis. Traefik e Portainer permanecem nas instalações existentes. O PostgreSQL é somente da Evolution; as escolas usam os bancos SQLite próprios do CFC+. Nenhuma porta 5050, 8080, 5432 ou 6379 é publicada.

Confira:
- app healthy; postgres e redis healthy; evolution running sem reinícios;
- `https://SEU_DOMINIO/api/health` retorna application cfc-plus, central true e versão 0.4.1;
- `https://SEU_DOMINIO/platform/` abre por HTTPS.

## 6. Criar a escola GP e entrar

Portainer → Containers → container app desta stack → Console → /bin/bash. Use o usuário padrão do container; não precisa root.

~~~sh
cat /data/platform-first-access.json
~~~

Consulte esse arquivo somente na console privada. Entre em /platform/ com platform-admin e a senha gerada. Troque a senha. Cadastre:
- ID: `gp-autoescola`;
- Nome: `GP Autoescola`.

Volte à console privada:

~~~sh
cat /data/tenants/gp-autoescola/first-access.json
~~~

Entre na página principal com ID gp-autoescola, login inicial da escola e a senha desse arquivo. Troque a senha. A importação mantém as duas unidades dentro desse mesmo ID; não crie uma escola separada por filial.

## 7. Importar a base da GP

Na gestão: **Instalação e importação**.
1. Envie o ZIP com a pasta 01_BANCO_DE_DADOS_COMPLETO e os SQL/CSV pareados e inventário.
2. Aguarde a conferência; revise relatório, alunos, unidades, agenda e totais financeiros.
3. Ative somente na escola vazia e após conferir os totais.
4. Entre novamente e revise os usuários importados antes de ativá-los.

Limite ZIP: 512 MB; expansão: até 8 GB. Reserve disco livre para pacote, staging, banco e backup. Se o pacote exceder isso, não corte tabelas para fazê-lo caber: precisamos ajustar o caminho de importação.

Importar SQL/CSV não transfere automaticamente fotos/PDFs que não estão no pacote. Arquivos protegidos por DPAPI e cookies do piloto Windows não se tornam válidos no Linux por simples cópia. Reconfigure credenciais e faça novos logins na VPS. Preserve o pacote original e o piloto anterior para conferência.

## 8. Configurar o WhatsApp desta escola

Nesta versão, as configurações Evolution por escola são arquivos privados do servidor; a interface oferece status, QR Code e conversas. Não há formulário completo para salvar URL/chave/instância. Faça a configuração inicial pela console privada do app.

Na console /bin/bash do app, após criar a GP:

~~~sh
umask 077
read -rsp 'Chave Evolution configurada no Portainer: ' evo_key
printf '\n'
[[ "$evo_key" =~ ^[a-fA-F0-9]{64}$ ]] || { echo 'Use a chave hexadecimal de 64 caracteres.'; exit 1; }
curl --fail --silent --show-error -H "apikey: $evo_key" -H 'Content-Type: application/json' \
  -d '{"instanceName":"gp-autoescola","integration":"WHATSAPP-BAILEYS","qrcode":true}' \
  http://evolution:8080/instance/create > /tmp/evolution-create.json
printf '{"Evolution":{"BaseUrl":"http://evolution:8080","Instance":"gp-autoescola","ApiKey":"%s"}}\n' "$evo_key" \
  > /data/tenants/gp-autoescola/evolution.config.json
unset evo_key
~~~

Se a instância já existe, confira-a antes de repetir; não apague o pareamento existente. A configuração JSON é carregada pelo app por escola. Reinicie somente app se necessário para que ela seja relida; mantenha volumes.

Na gestão, abra WhatsApp → Conectar e escaneie o QR pelo WhatsApp → Dispositivos conectados. Não exponha a Evolution na internet nem publique sua chave. Este piloto usa uma instância configurada por escola. Esse pareamento ainda precisa ser validado com o telefone real; infraestrutura saudável não significa conexão WhatsApp validada.

## 9. Baixar e instalar o app Windows

Caminho principal: [Releases do CFC+](https://github.com/zionLab7/cfc-plus/releases) → release **v0.4.1** → Assets → **CFC-Plus-Instalador-0.4.1-x64.exe**. A release só aparece depois que Publish versioned images → windows-release conclui.

Alternativa enquanto a release não estiver publicada: Actions → último Verify verde → Artifacts → CFC-Plus-Windows. É necessário entrar no GitHub para baixar o ZIP; extraia o .exe. Artefatos do Verify expiram em 14 dias.

Nas máquinas Windows x64:
1. Execute o instalador e mantenha o atalho CFC+.
2. Se o Windows indicar editor desconhecido, confira origem e checksum. O piloto ainda não tem assinatura comercial; não desative a proteção do Windows.
3. Selecione **Conectar a um servidor existente**.
4. Informe `https://SEU_DOMINIO`, sem /platform/ ou /portal/.
5. Entre com ID gp-autoescola, usuário pessoal e senha. Não escolha Iniciar servidor local: isso criaria outra base.
6. Em Integrações → Ver profissionais, use **Abrir neste computador** para o cartão conectado localmente; use **Abrir no servidor** quando quiser a sessão da VPS.
7. Em Integrações, gerente/admin pode usar **Verificar leitores deste computador**. Este diagnóstico não captura digitais nem certifica homologação.
8. Instale o middleware oficial do cartão e os drivers do leitor conforme o fabricante. Nunca envie PIN para o servidor.

O app usa perfis locais próprios; não importa cookies ou sessão do Infor. No primeiro uso será necessário entrar. A seleção de certificado apresenta os certificados que o Windows/middleware disponibilizar; confirme o titular e conclua o PIN na janela apropriada. Teste real do cartão e da biometria é obrigatório antes de depender desse fluxo em atendimento.

Para disponibilizar também pelo próprio domínio, obtenha o .exe aprovado e copie-o para /data/downloads/CFC-Plus-Windows.exe no volume do app, com leitura para UID 1654. Depois `https://SEU_DOMINIO/install/` mostra Baixar para Windows. A stack não baixa executáveis arbitrários automaticamente. O link direto GitHub Release dispensa essa cópia.

Aluno e instrutor usam o mesmo domínio e seus usuários vinculados. No celular: /portal/ → instalar PWA pelo navegador. Gestão também pode usar PWA, mas equipamentos locais e o botão de portal local exigem o cliente Windows desta entrega.

## 10. Backup, atualização e acompanhamento do mês

- Mantenha os nomes de volumes. Não remova volumes ao recriar a stack.
- Faça backup diário externo e teste restauração. Para o volume central, use infra/production/backup.sh central /CAMINHO_PRIVADO_DE_BACKUPS cfc-gp-central-data. Esse backup para somente os containers que usam esse volume e causa uma janela de indisponibilidade do CFC+.
- Evolution tem volumes próprios. Guarde também dump consistente do PostgreSQL e os dados de instâncias/Redis. O backup central não inclui WhatsApp.
- Para copiar consistentemente a Evolution, pare evolution e redis, faça pg_dump enquanto PostgreSQL está ligado e arquive os volumes de instâncias/Redis. Em caso de erro, reinicie os serviços parados. Não copie o volume bruto do PostgreSQL ligado.
- Exporte/guarde privadamente as variáveis do Portainer. Segredos e backups nunca vão para o GitHub.
- Confira diariamente reinícios/OOM, CPU/RAM, disco, falhas de login e tempos de busca/agenda/financeiro. OOMKilled e unhealthy pedem diagnóstico; restart: unless-stopped não reinicia automaticamente um processo só porque está unhealthy.
- Antes de atualizar: backup, Verify verde, publicação da nova versão e janela de manutenção. Atualize CFC_BROWSER_IMAGE manualmente; preserve os volumes.
- A outra aplicação da VPS não deve ser parada. Se o conjunto competir por recursos, reduza navegadores ou faça upgrade após medir.

## Diagnóstico rápido

| Sintoma | Conferir |
|---|---|
| manifest unknown / denied no GHCR | Workflow publish, tag e visibilidade/permissão do pacote. |
| network cfcplus not found | Criar rede e conectar Traefik persistentemente. |
| 404 do Traefik | Hostname, entrypoint, labels e provider Docker. |
| 502/504 | app healthy e Traefik conectado na mesma rede. |
| Login manda usar HTTPS mesmo com cadeado | TRAEFIK_IP incorreto, IP mudou, ou cabeçalho encaminhado por outro proxy. |
| Certificado HTTPS inválido | DNS, resolver, desafio ACME e portas 80/443 existentes. |
| Navegador de servidor falha ao iniciar | Logs app, seccomp/user namespaces e recursos da VPS; não desative sandbox para esconder a falha. |
| Leitor local não aparece | Instalador atualizado, driver, serviço Cartão Inteligente e diagnóstico local. |
| Biometria não captura | Integração SDK/protocolo pendente; navegador local não é um adaptador biométrico por si só. |
| App reinicia / OOMKilled | Memória efetivamente disponível, perfis abertos, importação e a outra aplicação. |

Referências: [Portainer Git stacks](https://docs.portainer.io/user/docker/stacks/add), [Traefik Docker](https://doc.traefik.io/traefik/reference/install-configuration/providers/docker/), [artefatos GitHub](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts).
