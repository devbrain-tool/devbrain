!macro customInstall
  nsExec::ExecToLog 'setx PATH "%PATH%;$INSTDIR\resources\bin"'
!macroend

!macro customUnInstall
  ; PATH cleanup is complex in NSIS — users can manually clean up
!macroend
