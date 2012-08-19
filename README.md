CSharp-LoadTestEngine
=====================

An engine written in C# used for load testing a web application.

Usage:
------
The 'sampleTest.xml' file contains example for the correct structure of a test that consists of 2 requests, a POST to login to a site with a username and password, followed by a GET to simulate navigating to a subsequent page or url.  The 'configuration node handles the typical information for generating a load - threas (simulated users), duration (seconds or 'pulse' for a one-time run of 'x' threads, where 'x' is the number of threads specified).

Notes:
------
The 'rampUp' setting in the 'configuration' node is not currently used.  This functionality will be added at a later date.

When a POST is required, the 'url' specified must be the location of where the form is submitted.  Eventually, the user will only have to specify the location of the page that performs the submit, with the app finding the location itself.
