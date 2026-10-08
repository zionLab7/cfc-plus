# Capacidade e evolução da plataforma central

A versão 0.4 tem uma instância central e bancos SQLite independentes por escola. Escritas em escolas diferentes não compartilham o semáforo do Store. Consultas usam índices e páginas, e os totais financeiros são calculados sobre o conjunto completo no servidor. O cadastro de mais escolas não cria outro deploy. Fila e seleção offline são vinculadas à escola/usuário.

node tests/central-http.mjs mede consultas concorrentes a duas escolas SINTÉTICAS e grava p95. Esse teste comprova isolamento e recuperação, mas não dimensiona uma base real nem garante capacidade de VPS. Não use quantidade de escolas/alunos sozinha como medida: usuários simultâneos, documentos, importações e navegadores abertos dominam o consumo.

## Homologação

Comece medindo uma VPS Linux com CPU dedicada e NVMe na região adequada. O exemplo da stack reserva 6 CPUs/16 GB para a aplicação central; o host precisa de margem para Linux, Traefik, Portainer, Evolution e backups. Ajuste após teste com o export autorizado, buscas, agenda, ficha e financeiro reais. Esses valores são ponto de homologação, não promessa de capacidade ou recomendação de fornecedor.

Importações: no máximo duas conferências em paralelo no servidor. Navegadores: padrão global 12 perfis e até 6 por escola, configuráveis. Feche perfis sem uso antes de aumentar limite; meça memória real de Chromium e pico das importações. O limite recusa nova abertura sem desalojar uma sessão em uso.

Meça p50/p95, erros, CPU, RAM, disco e filas no pico previsto; teste documentos, navegador, queda/reconexão e restauração. Estabeleça metas de latência antes de comercializar. Aumentar limite de contêiner sem recursos no host não cria capacidade.

## Próxima escala

Agora há um único servidor escritor sobre o volume central. Mais de uma réplica exigirá evoluir o registro central, armazenamento e coordenação; não basta aumentar replicas no Compose. O caminho é PostgreSQL com resolução/autorização de tenant em todas as operações, filas externas, armazenamento de documentos e workers de browser com roteamento estável para o dono da sessão. Chaves e sessões precisam de armazenamento persistente e política de expiração.

Podemos preservar o mesmo domínio e app ao separar esses serviços internamente. A distribuição por escola fica na infraestrutura/dados, sem exigir instalação/deploy por cliente. Essa evolução distribuída ainda não está implementada; primeiro valide a operação central e o perfil de carga real. Backups completos hoje têm janela de parada central; recuperação por escola e backups online consistentes são parte da evolução operacional.
