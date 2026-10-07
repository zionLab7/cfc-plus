# Capacidade e expansão

A arquitetura atual é uma instância isolada por autoescola, com SQLite em disco local, índices, consultas por pessoa/período e listagens paginadas. O aluno/instrutor usa consultas próprias, sem carregar diretório global nem credenciais da equipe. A agenda evita uma consulta adicional por aluno ao montar o período. A importação fica em outro processo e tem só um worker por escola. Sessões interativas possuem limite de perfis configurável.

O teste local de 30 consultas concorrentes à base **vazia e sintética** teve p95 de 56 ms. Esse dado é apenas uma medição de referência do desenvolvimento; não dimensiona a base completa nem garante desempenho em uma VPS. O resultado é reproduzível por `node tests/commercial-http.mjs`, que grava um relatório sem dados reais.

## Começar

Uma VPS Linux com CPU dedicada, NVMe e boa latência para São Paulo é a base. A estimativa anterior de 8 vCPU, 32 GB RAM e 200 GB NVMe pode servir para começar a homologação, mas a quantidade de escolas depende de pessoas conectadas, tamanho de cada base e perfis de navegador abertos. Reserve recursos para Linux, Traefik, Portainer e backup; não divida toda a RAM entre contêineres.

A stack sugere limite inicial de 2 CPUs e 4 GB por escola; isso é uma configuração inicial para medir, não uma capacidade garantida. O modo com navegador deve ter orçamento maior conforme o número de sessões. Monitore o pico de RAM da importação de cada escola antes de ativar. O número de alunos sozinho não é uma boa unidade de capacidade.

## Roteiro de carga antes de vender

1. Importe uma cópia autorizada da escola no ambiente de homologação.
2. Meça busca, agenda diária/semanal, ficha, financeiro e portal com o número previsto de atendentes e alunos simultâneos.
3. Teste também navegador, envio de documentos e importação fora do pico.
4. Registre p50/p95, erros, CPU, memória, disco e crescimento dos arquivos. Estabeleça meta de atendimento e ajuste recursos até cumpri-la.
5. Faça restauração, reinício e indisponibilidade de rede para conferir recuperação e fila offline.

## Mais clientes

Provisione escola por escola com domínio/ID/volume exclusivos. Distribua as stacks entre VPS conforme medições; uma escola pesada pode ter VPS própria sem mudar o acesso dos demais clientes. Git/CI mantém uma versão comum do produto; dados nunca acompanham deploy de código.

O banco atual não suporta escalar uma mesma escola com vários servidores escrevendo simultaneamente. Antes dessa necessidade, implemente migração para PostgreSQL com tenant e autorização em todas as consultas, armazenamento de documentos, workers/filas dedicados e testes de isolamento e carga. Nada disso está configurado automaticamente nesta versão. As sessões de navegador exigem roteamento estável ao worker que as possui, armazenamento persistente e tratamento de expiração; não devem ser distribuídas aleatoriamente por um balanceador.
