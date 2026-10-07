# Uso e distribuição

## Um endereço e acessos pessoais

Cada escola divulga seu domínio HTTPS, por exemplo `https://escola.seudominio.com.br`. Todos usam esse endereço. O sistema reconhece o perfil após o login. O administrador cria acessos em Configurações → Usuários; para aluno/instrutor, pode usar Criar acesso na central Portais, com vínculo à ficha já selecionada. Não é necessário cadastrar o aluno novamente.

O usuário recebe login e senha inicial da escola por um canal adequado, entra e escolhe sua senha pessoal. O administrador pode redefinir uma senha se o titular perder acesso. Ainda não há recuperação automática por email nem envio automático dos convites; não inclua esses procedimentos na promessa comercial desta versão.

## Celular e tablet

Abra o domínio e a página **Instalar no celular ou computador** (`/install/`). No Android, use Chrome → Instalar aplicativo / Adicionar à tela inicial. No iPhone/iPad, abra no Safari → Compartilhar → Adicionar à Tela de Início. O navegador pode variar o nome dos comandos.

É uma PWA, sem App Store/Google Play nesta fase. Depois de instalar, abre em janela própria. Ela continua ligada ao servidor; instalar não copia o banco para o celular. Os portais não guardam dados pessoais em cache offline.

## Computador

Windows, Mac e Linux podem usar o navegador ou instalar a PWA. O atendimento Windows também pode baixar o EXE exibido em `/install/`, instalar o CFC+, escolher **Conectar ao servidor** e informar o domínio HTTPS da escola. Os clientes não precisam instalar .NET nem executar um banco local.

O instalador não inclui dados da GP. A opção de servidor local do cliente Windows atende desenvolvimento/instalações locais planejadas; não é a configuração usada pelos funcionários quando a escola opera na VPS. Não distribua um diretório App_Data nem acesso inicial do administrador junto do instalador.

## Aluno

Agenda e exames, próxima aula, confirmação de intenção de comparecer, andamento da matrícula, avaliações compartilhadas pelo instrutor, financeiro, documentos e envio de PDF/foto. Solicitações de remarcação, disponibilidade, documentos e dúvidas chegam à escola e recebem resposta na própria área. A remarcação depende da escola e das regras de agenda; não cria uma reserva automaticamente.

## Instrutor

Agenda pessoal, alunos vinculados no período, histórico, calendário, registro interno de aula/falta, avaliação por quatro habilidades, orientações públicas e notas privadas, além de solicitações para a escola. O instrutor não recebe acesso às senhas GOV.BR, ao financeiro ou aos documentos privados dos alunos. Registrar uma aula internamente não valida a presença no DETRAN.

## Gestor/administrador

Acesso à gestão no computador e celular, configurações, cadastros, agenda, financeiro, central de solicitações e portais em modo de visualização. A importação e criação de acessos são administrativas. As sessões governamentais continuam no servidor quando o cliente fecha; o profissional pode precisar renovar o login quando o portal exige.

Antes de vender com uso real, faça o roteiro de aceite com a escola: acesso de cada perfil, conferência da importação e saldos, mudança de senha, upload/download, remarcação sem conflito, backup/restauração e teste do GOV.BR nesse servidor. Hardware externo e ações oficiais ainda exigem validação própria.
