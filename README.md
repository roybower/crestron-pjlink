# crestron-pjlink 

SIMPL# Pro class to control projectors via [PJLINK](https://pjlink.jbmia.or.jp/english/data/5-1_PJLink_eng_20131210.pdf) protocol


## Example Usage
```c#
PJLink projector = new PJLink(); 

projector.PJLinkInitialise(projectorIP);

if (projector.PJLinkPowerState == PJLink.projOff)
    projector.PJLinkPower(true);  /// turn on
```


## Contributing
Pull requests are welcome. 

## License
[GPL-3.0](https://choosealicense.com/licenses/gpl-3.0/)