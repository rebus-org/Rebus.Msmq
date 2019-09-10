# Changelog

## 2.0.0-b01

* Test release

## 2.0.0-b02

* Made namespaces more convenient - also for people without R#

## 2.0.0

* Release 2.0.0

## 2.0.1

* Fix bug where `MessageQueueTransaction` was committed in the wrong place. Could lead to subtle race conditions between the user's code and the MSMQ transaction when adding `OnCommitted(...)` callbacks to the transaction context

## 3.0.0

* Update to Rebus 3
* Remove weird legacy mode UTF7 stuff

## 4.0.0

* Update to Rebus 4
* Add .NET Core support
* Fix typo ;)

## 5.0.0

* Update to Rebus 5
* Target .NET 4.5 and .NET 4.6

## 5.1.0

* Enable custom serialization of headers by introducing the `IMsmqHeaderSerializer` service - thanks [hjalle]

[hjalle]: https://github.com/hjalle