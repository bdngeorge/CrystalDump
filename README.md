# CrystalDump
A small console app to dump data from a Crystal Report, with the ability to filter what is dumped.

## Usage

1. Download the zip file containing the app and extract to a location of your choosing.
2. Add the path of the CrystalDump directory to your PATH (user or system)
	a. Make sure to restart any terminal session before use
3. Run a dump with the following command: `CrystalDump <path/to/report.rpt> --dump -o <path/to/output>`
	a. If you want to search for a specific term, add `-s comma,delimited,list,of,terms` 