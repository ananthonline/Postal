# Postal
Postal is a code generator that generates strongly typed message contracts for request-response type protocols. Messages are serialized and deserialized using Protocol buffers.

See the included sample (Postal.Test, Postal.Test.Client, Postal.Test.Server) for usage.

* The generated cs file is in the /obj/<Configuration> directory, named <yourpostalfilename>.postal.cs
* The generate proto file is also in the /obj/<Configuration> directory, named <yourpostalfilename>.postal.proto