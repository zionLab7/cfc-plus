# CFC+ — plataforma de gestão de autoescolas

Servidor .NET 10, interface web/PWA e cliente Windows Electron. A versão 0.3.0 inclui instalação comercial vazia, isolamento por autoescola, portais de aluno/instrutor, agenda, financeiro, anexos protegidos, configuração administrativa e importação conferida de exports Infor SQL/CSV.

Cada cliente usa seu domínio, processo e volume. Filiais da mesma autoescola compartilham a instalação. O repositório contém código e fixtures sintéticas; não contém banco, exportação, documentos, senhas ou sessões da GP.

## Implantar

Leia [o guia de implantação](docs/IMPLANTACAO.md). Há duas stacks para Portainer Docker Standalone:

- `infra/production/compose.yaml`: gestão e portais sem navegador governamental.
- `infra/production/compose.browser.yaml`: stack completa com navegador interativo no Linux e sessões persistentes. Exige o perfil seccomp no host, conforme o guia. Não usa contêiner privilegiado, CDP/WebDriver para o login nem desativa o sandbox Chromium.

As imagens são geradas pelo [Dockerfile](Dockerfile). O workflow `Publish versioned images` publica versões no GHCR mediante tag ou execução manual. O workflow `Verify` verifica aplicação, importação sintética e navegador Linux, além de produzir o instalador Windows.

## Para os usuários

Cada autoescola divulga seu endereço HTTPS. O aluno e instrutor entram e são direcionados ao próprio portal. Gerentes, atendentes e administradores acessam a gestão. O guia `/install/` explica como instalar no Android, iPhone, tablet, Windows, Mac e Linux. A primeira senha comercial precisa ser substituída pelo titular. A escola cria acessos vinculados às fichas; não existe cadastro público livre.

## Importar depois de publicar

Em **Instalação e importação**, o administrador envia um ZIP com a pasta `01_BANCO_DE_DADOS_COMPLETO`, SQL/CSV pareados e inventário. O envio é retomável; a conferência roda em processo separado e mantém a base ativa disponível. Depois da revisão, uma instalação vazia pode ativar o resultado e reiniciar. Bases já operacionais não são substituídas pela interface.

[Formato, revisão e recuperação da importação](docs/IMPORTACAO.md).

## Desenvolvimento e verificação

Requisitos: SDK .NET 10, Node 22.12+ para testes/cliente; Docker Linux para validar implantação. O modo padrão de desenvolvimento usa somente dados fictícios se o diretório de dados estiver vazio. Nunca configure desenvolvimento no volume de um cliente.

```sh
dotnet build
dotnet bin/Debug/net10.0/CfcPilot.dll --self-test-commercial
dotnet bin/Debug/net10.0/CfcPilot.dll --self-test-community
node tests/commercial-http.mjs
node tests/integration.mjs
CFC_TEST_RELATIONAL=1 node tests/integration.mjs
```

O cliente Windows usa `desktop/pnpm-lock.yaml`. Execute `pnpm install --frozen-lockfile`, `node node_modules/electron/install.js`, publique o servidor em `artifacts/server/windows-x64` e execute `pnpm run build:win` dentro de `desktop`.

## Operação e limites

[Uso e distribuição](docs/USO_E_DISTRIBUICAO.md), [capacidade e expansão](docs/ESCALA.md), [backup e segurança](docs/OPERACAO.md).

A implantação Docker foi validada localmente com dados sintéticos. Publicação na VPS, DNS, certificado HTTPS e login GOV.BR real nesse servidor precisam de validação no ambiente definitivo. Registro interno de aula não confirma aula no DETRAN. Tokens e biometria físicos ainda dependem de homologação e do componente local de hardware; não são acessíveis automaticamente pela VPS. Algumas tabelas legadas têm consulta integral, sem editor específico. Mensagens push, publicação em App Store/Google Play e migração para PostgreSQL não estão implementadas nesta versão.
