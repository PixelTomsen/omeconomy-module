; ***** OMEconomy for OpenSim (PayManager www.virwox.com) ****
;
;Orginal Project Page : http://forge.opensimulator.org/gf/project/omeconomy
;Developer : Michael Erwin Steuer
;Code-Version : 0.03.0003
;
;Version : Prebuild, Simulator-Version 0.7.3
;Start : 2011-10-26 - Pixel Tomsen (chk) (pixel.tomsen [at] gridnet.info)
;
;git-Source: https://github.com/PixelTomsen/omeconomy-module
;

todo :
- copy folder addon-modules to source-folder-of-opensim
- run prebuild
- run compile/xbuild

-----------------------------------------------------------------------------------------
mono hint
startup-exception:  Invalid certificate received from server (mono issue for missing certificates) 

Linux
#shell: sudo mozroots --import --sync

Windows:
mozroots.exe --import --sync
-----------------------------------------------------------------------------------------

:Add following Lines to OpenSim.ini:


[OpenMetaverseEconomy]
  ;# {Enabled} {} {Enable OMEconomy} {true false} false
  enabled = true
  ;;
  OMEconomyInitialize = "https://www.virwox.com:419/OSMoneyGateway/init.php"
  ;;
  ;;Test System
  ; OMBaseEnvironment = "TEST"
  ; OMCurrencyEnvironment = "TEST"
  ;;
  ;;Productive System
  OMBaseEnvironment = "LIVE"
  OMCurrencyEnvironment = "LIVE"