# Backend

This solution is an `ASP.NET` C# HTTP Web API. It contains a series of controllers and endpoints that perform CRUD operations on a local SQLITE DB through entity framework.

You can see the DB schema in `Database/spec.md`

For each table in the database (except the join entities), there are endpoints to create, read, update, and delete rows in the DB. The endpoints for reading for the tables marked 'Big' in the schema support pagination since there are too many rows to load at once. For the 'Small' tables it is safe to load everything. The endpoints support ordering by any column depending on the request. For any ordering request, if the names are the same the second column to order by is ID.

There are DTOs for receiving requests from the frontend to do these operations and sending back success or error responses.
