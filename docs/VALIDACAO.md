# Validação e limitações

## Verificação no hardware de desenvolvimento — 05/10/2026

- Ryzen 7 5700X: leitura de oito valores CO, FMax 4750 MHz, temperatura Tctl/Tdie direta e temperatura do CCD.
- Interface WPF .NET 8: renderização e valores da CPU confirmados visualmente pelo usuário.
- Uso total da CPU calculado pelos tempos reais do Windows, sem valor fixo.
- Telemetria com atualização aproximadamente a cada dois segundos.
- Stock: oito CO 0, FMax preservado em 4750 MHz, limite GPU 170 W, plano Equilibrado e três curvas automáticas de ventoinhas da placa-mãe restauradas e relidas.
- RTX 3060 sob FurMark: utilização de 97–98%, potência próxima do teto de 187 W e limitador de potência ativo; clock de 2100 MHz não confirmado. O resultado não certifica o perfil Máximo.
- Após Stock, Tctl/Tdie ficou em aproximadamente 74–75 °C com 15–19% de uso em segundo plano. Isso não foi uma medição em idle verdadeiro; não se concluiu a causa física do aquecimento.

## Verificações internas do backend

`--self-test` executa 12 verificações sem abrir o adaptador de hardware:

1. Emergência térmica da CPU.
2. Emergência térmica da GPU.
3. Redução de nível pela CPU.
4. Redução de nível pela GPU.
5. Resfriamento sem aumento automático.
6. CO diferente entre núcleos não confirmado.
7. Quantidade incorreta de núcleos não confirmada.
8. Falha de SMU não interpretada como Stock.
9. GPU sob carga e limitada por potência não descrita como idle.
10. Duas amostras exatas insuficientes para confirmar clock.
11. Três amostras exatas sob carga confirmam a leitura do clock.
12. GPU ociosa mantém validação pendente.

## Adaptação para publicação

- Referência relativa para a cópia fixada de ZenStates-Core.
- Caminho configurável para LibreHardwareMonitor.
- Verificações explícitas do modelo da CPU e GPU.
- Captura inicial de baseline apenas por leitura e gravação local, recusando estado não Stock e arquivo já existente.
- Scripts de compilação/instalação separados dos dados locais.

Compilação e verificações internas exercitam o código, mas não demonstram que uma instalação limpa em outro computador vai funcionar. O instalador de distribuição não foi executado sobre a instalação de desenvolvimento em uso. As novas recusas de modelo e a captura inicial dependem de verificação em uma instalação separada.

A compilação da cópia fixada de ZenStates-Core emite uma mensagem de arquivo legado `inpoutx64.dll` ausente no pós-build e prossegue. Essa cópia exige que o caminho PawnIO utilizado nesta configuração esteja disponível; a mensagem não demonstra funcionamento em uma máquina sem o driver.

## Pendências

- Estabilidade prolongada de cada perfil de desempenho e critérios completos de aprovação.
- Instalação limpa e comportamento em outras placas-mãe, drivers e variantes de RTX 3060.
- Leitura direta da política automática de ventoinha NVIDIA.
- Causa e temperatura do processador em idle verdadeiro.
- Recuperação em falhas externas de driver, processo ou serviço.

Nenhuma dessas pendências deve ser tratada como resultado aprovado.
