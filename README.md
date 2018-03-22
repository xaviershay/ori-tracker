Ori Tracker
===========

Live traces of Ori runs. It uses a desktop component similar to the livesplit
auto-splitter to capture Ori's position, then sends it to a remote server for
plotting on a map.

Also included is a map stitcher to create the map from in-game screenshots. It
technically works, though has a lot of hard-coded nonsense and will require
some hacking to get working on your machines.

Development
-----------

These are just some quick notes, better documentation is needed.

* You'll need a firebase account and the firebase CLI tools. The CLI tools will prompt you for login.
* The CLI tools should enable you to serve up static files and cloud functions at localhost. The client will need modification to post to these URLs.
* The client should compile and run with the free version of Visual Studio 2017.
* If you poke around the code you'll find bit ands pieces for generating fake data, which helps with testing.
