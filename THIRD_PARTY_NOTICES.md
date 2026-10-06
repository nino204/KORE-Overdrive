# Dependências e créditos

## ZenStates-Core

- Projeto: <https://github.com/irusanov/ZenStates-Core>
- Autoria: irusanov e contribuidores.
- Revisão fixada: `869b7e82572337e5a673e92230edc92bddf0e44b`.
- Licença principal: GPL-3.0; texto preservado em `vendor/ZenStates-Core/LICENSE`.
- A cópia em `vendor/ZenStates-Core` corresponde aos arquivos versionados dessa revisão e não inclui dados da máquina de desenvolvimento. Não foram modificados os arquivos dessa cópia.
- Arquivos de terceiros incluídos pelo projeto mantêm seus avisos originais. Consulte também `vendor/ZenStates-Core/External/` e os cabeçalhos dos arquivos-fonte.

## LibreHardwareMonitor

- Projeto: <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor>.
- Usado para controle das ventoinhas da GPU, pela DLL instalada com Fan Control.
- Licença principal do projeto: MPL-2.0; componentes adicionais podem ter outros termos, descritos pelo upstream.
- A DLL não é incluída neste repositório. O caminho é informado na compilação. A configuração testada usou a biblioteca 0.9.6.0 fornecida pela instalação local de Fan Control.

## Componentes externos

- Fan Control: <https://github.com/Rem0o/FanControl.Releases>.
- PawnIO: <https://pawnio.eu/>. O instalador do driver não é redistribuído neste repositório.
- NVIDIA `nvidia-smi`: fornecido pelo driver NVIDIA instalado pelo usuário.
- Gigabyte EasyTune Engine Service: DLLs e serviço instalados pelo usuário; as DLLs proprietárias não são incluídas.
- .NET e os pacotes NuGet referenciados mantêm suas licenças originais.

O código do KORE Overdrive não transfere a propriedade nem altera a licença desses componentes.
