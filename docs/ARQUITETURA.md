# Arquitetura

## Interface

`src/KOREOverdrive/Program.cs` monta a janela WPF em .NET 8 e força renderização por software. Um mutex impede janelas duplicadas; uma segunda execução sinaliza a janela existente. As chamadas HTTP são assíncronas.

A interface pede a execução da tarefa `KOREOverdriveBackend`, lê o token da conta Windows e consulta o estado. Os botões de perfil enviam `POST /profile/0` a `/profile/3`; Emergency usa `POST /emergency`. `GET /state` devolve o último snapshot.

## Backend

`src/KOREOverdrive/Backend/Program.cs` executa elevado em .NET 10. Um mutex global limita o backend a uma instância no computador. O listener fica restrito ao endereço loopback. O token é recriado a cada execução, tem ACL específica e não deve aparecer em logs ou relatórios públicos.

Um semáforo serializa polling e aplicação de perfis, evitando concorrência do próprio backend no acesso ao SMU. A emergência sinaliza uma aplicação em andamento e solicita Stock entre etapas de escrita. Isso não coordena ferramentas externas que também escrevam no hardware.

## Adaptadores

- **CPU:** subprocesso `KORECpuTuner`, baseado em ZenStates-Core/PawnIO. Lê CO de cada núcleo, FMax, Tctl/Tdie e CCD. Escreve somente CO nos comandos de perfil. `capture-baseline` salva apenas um baseline local já confirmado como CO 0 / FMax 4750.
- **Uso da CPU:** `CpuUsage.cs` calcula a diferença dos tempos de sistema via `GetSystemTimes`.
- **GPU:** `nvidia-smi`, índice 0, controla lock de clock e limite de potência e lê o resultado. LibreHardwareMonitor controla ventoinhas quando há sensores de controle disponíveis.
- **Placa-mãe:** `GigabyteBridge.ps1` carrega a API .NET Framework do EasyTune Engine Service em um processo persistente de Windows PowerShell 5.1. A troca ocorre por stdin/stdout, uma solicitação por vez.
- **Plano de energia:** `powercfg` seleciona Equilibrado ou Alto desempenho.

## Verificação e proteção

`Shared.cs` reúne perfis, identificação de CO uniforme, regras térmicas e validação do clock da GPU. O backend compara alvo e leitura. Em Stock, procura observar variação de clock após remover o lock; em desempenho, exige três leituras exatas com uso da GPU de pelo menos 20%.

Uma temperatura ausente não vira zero. O guard reduz perfis ativos quando sensores essenciais falham ou quando limites térmicos são atingidos. Ele não aumenta perfis sozinho. A proteção depende do backend e dos drivers disponíveis; não substitui proteções do firmware.

## Distribuição

A árvore pública usa referências relativas para ZenStates-Core e uma propriedade de compilação para a DLL externa do LibreHardwareMonitor. O instalador cria uma tarefa sob a conta interativa do usuário, sem trigger de logon. Não acompanha perfis ou baselines capturados em outra máquina.
