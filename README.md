# CFC+ — plataforma central de autoescolas

Versão 0.4.1: um servidor .NET 10 e um único app web/PWA para todas as autoescolas. Cada escola tem um ID, banco SQLite, anexos, configuração Evolution e perfis governamentais próprios. As filiais têm subIDs (unitId) dentro da escola. Entrar com ID da escola + usuário + senha determina o contexto no servidor; aluno e instrutor seguem para o portal pessoal.

O repositório público contém somente código e fixtures sintéticas. Bancos, documentos, exports, credenciais e sessões da GP permanecem privados.

## Implantação central

Use UMA stack no Portainer Docker Standalone, com Traefik, domínio central HTTPS e volume persistente. Para o teste da GP em KVM 2, siga o [guia completo do Portainer e Windows](docs/PORTAINER_PILOTO_GP.md). Consulte também [implantação](docs/IMPLANTACAO.md).

- infra/production/compose.pilot.yaml: stack única do piloto com app, navegador, Evolution, PostgreSQL da Evolution e Redis; reutiliza Traefik existente e incorpora seccomp.
- infra/production/compose.yaml: gestão e portais.
- infra/production/compose.browser.yaml: também inclui navegador interativo Linux/Xvfb, cookies persistentes e sandbox Chromium. Exige o perfil seccomp no host.

CFC_MULTI_TENANT=true habilita o modo central; as imagens Docker já usam esse padrão. /platform/ é o painel do dono da plataforma para criar IDs de escolas sem novo deploy. / é a gestão, /portal/ é o portal pessoal e /install/ explica a instalação.

## Financeiro

A ficha mostra débitos, dinheiro efetivamente pago, créditos/descontos, saldo em aberto, vencido e a vencer. Permite recebimento parcial numa parcela específica, geração de recibo e estorno com histórico preservado. Divergências entre extrato e parcelas importadas aparecem para conciliação.

O financeiro geral separa caixa do período e carteira atual de todas as datas, permite filtro por filial, lista prioridades de cobrança e oferece extrato paginado e CSV da página. Estornos de desconto não reduzem caixa; crédito de um aluno não compensa dívida de outro.

## Importar e distribuir

Cada administrador importa somente na sua escola pelo menu Instalação e importação. A conferência roda em staging e a ativação exige base vazia. Recarrega somente a escola afetada; os demais clientes continuam atendidos. [Formato e recuperação](docs/IMPORTACAO.md).

Todos instalam o mesmo app e recebem ID, usuário e senha da escola. [Celular, portal e cliente Windows](docs/USO_E_DISTRIBUICAO.md).

## Desenvolvimento e testes

SDK .NET 10, Node 22.12+ e Docker Linux. Modo padrão sem CFC_MULTI_TENANT mantém a demonstração sintética; use um diretório vazio de teste.

~~~sh
dotnet build
dotnet bin/Debug/net10.0/CfcPilot.dll --self-test-finance
dotnet bin/Debug/net10.0/CfcPilot.dll --self-test-commercial
dotnet bin/Debug/net10.0/CfcPilot.dll --self-test-community
node tests/central-http.mjs
node tests/commercial-http.mjs
node tests/integration.mjs
CFC_TEST_RELATIONAL=1 node tests/integration.mjs
~~~

O teste central verifica duas escolas com o mesmo login/senha e CPF, filiais, credenciais protegidas, importações, financeiro, acesso pessoal, concorrência e reinício. Os testes usam apenas fixtures sintéticas. Verify gera também o instalador Windows; Publish versioned images publica no GHCR por tag ou execução manual após validação. A versão de imagem nos exemplos precisa estar publicada ou construída antes do deploy.

Para desenvolver centralmente em localhost, configure CFC_MULTI_TENANT=true, CFC_DATA_DIR num diretório de teste, ASPNETCORE_ENVIRONMENT=Development e CFC_ALLOW_LOCAL_HTTP=true. A exceção HTTP só vale para loopback em Development; não habilita HTTP público em produção.

[Operação e backups](docs/OPERACAO.md) · [Capacidade e evolução](docs/ESCALA.md)
