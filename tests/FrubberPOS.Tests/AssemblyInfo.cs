using Xunit;

// JHO-113: todas las pruebas de integración comparten una única base
// PostgreSQL real (frubberpos_test) y la limpian entre clases, así que
// deben ejecutarse en serie. Sin esto, dos clases se truncarían entre sí.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
